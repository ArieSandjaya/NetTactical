using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Push;

/// <summary>
/// Pushes the agent to remote computers as a domain administrator. Singleton: jobs live in memory only (lost on restart).
/// The credential exists only in the running task; it is never put in a job, the database, the audit log or a log line.
/// If one computer rejects the credential the rest of the job is skipped, so a wrong password cannot lock the domain account.
/// </summary>
public sealed class PushInstallService(IServiceScopeFactory scopes, IRemoteExecutor remote, IOptions<TacticalOptions> opt, ILogger<PushInstallService> log) : IPushInstallService
{
    private const int MaxJobsKept = 30, MaxRunning = 2;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(6);

    private sealed class Job
    {
        public required string Id { get; init; }
        public required int OwnerId { get; init; }
        public required string ClientName { get; init; }
        public required string SiteName { get; init; }
        public required string Username { get; init; }
        public required bool Force { get; init; }
        public required DateTime CreatedAt { get; init; }
        public DateTime? FinishedAt { get; set; }
        public string State { get; set; } = PushInstall.Running;
        public required List<Target> Targets { get; init; }
        public required List<string> Warnings { get; init; }
        public readonly object Gate = new();
    }
    private sealed class Target { public required string Host { get; init; } public string Status { get; set; } = PushInstall.Queued; public string? Message { get; set; } public string? Category { get; set; } }

    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    public async Task<Outcome<PushJobDto>> StartAsync(User caller, PushInstallRequest req, string defaultApi, CancellationToken ct = default)
    {
        if (!InstallerCommands.AgentTypes.Contains(req.AgentType)) return Outcome.Invalid<PushJobDto>("agent_type must be workstation or server");
        if (!InstallerCommands.Architectures.Contains(req.Arch)) return Outcome.Invalid<PushJobDto>("arch must be amd64 or 386");
        if (InstallerCommands.NatsPortError(req.NatsPort) is { } npe) return Outcome.Invalid<PushJobDto>(npe);
        if (PushInstall.ValidateUsername(req.Username) is { } ue) return Outcome.Invalid<PushJobDto>(ue);
        if (PushInstall.ValidatePassword(req.Password) is { } pe) return Outcome.Invalid<PushJobDto>(pe);
        var (hosts, terr) = PushInstall.ParseTargets(req.Targets);
        if (terr is not null) return Outcome.Invalid<PushJobDto>(terr);
        var api = string.IsNullOrWhiteSpace(req.Api) ? defaultApi : req.Api;
        if (InstallerCommands.ValidateApi(api) is { } ae) return Outcome.Invalid<PushJobDto>(ae);
        api = InstallerCommands.NormalizeApi(api);
        if (api.Contains("localhost", StringComparison.OrdinalIgnoreCase) || api.Contains("127.0.0.1"))
            return Outcome.Invalid<PushJobDto>("The server URL points to localhost, which the target computers cannot reach. Enter the server's address in the form.");

        var version = opt.Value.LatestAgentVer;
        var url = InstallerCommands.DownloadUrl(version, req.Arch);
        if (url is null) return Outcome.Conflict<PushJobDto>($"Configured agent version '{version}' is not valid.");

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var site = await db.Sites.Include(s => s.Client).AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SiteId && s.ClientId == req.ClientId, ct);
        if (site is null) return Outcome.NotFound<PushJobDto>("Client or site not found");
        if (!PermissionEvaluator.CanSeeSite(caller, site)) return Outcome.Forbidden<PushJobDto>();
        if (!await db.Users.AnyAsync(u => u.IsInstallerUser && u.IsActive, ct)) return Outcome.Conflict<PushJobDto>("There is no installer account (it is created on first start).");

        Prune();
        if (_jobs.Values.Count(j => j.State == PushInstall.Running) >= MaxRunning) return Outcome.Conflict<PushJobDto>("Two pushes are already running. Wait for one to finish.");

        var warnings = new List<string>();
        if (!api.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) warnings.Add("The server URL is not https.");
        var job = new Job
        {
            Id = Guid.NewGuid().ToString("N"), OwnerId = caller.Id, ClientName = site.Client.Name, SiteName = site.Name, Username = req.Username.Trim(), Force = req.Force,
            CreatedAt = DateTime.UtcNow, Targets = hosts.Select(h => new Target { Host = h }).ToList(), Warnings = warnings,
        };
        _jobs[job.Id] = job;

        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await audit.LogAsync(caller.Username, AuditAction.AgentInstall, "agent",
            $"{caller.Username} started a push install of the agent to {hosts.Count} computer(s) for {site.Client.Name} / {site.Name} as {job.Username}{(req.Force ? " (force)" : "")}: {string.Join(", ", hosts)}", ct: ct);

        var password = req.Password;
        var run = new RunArgs(job, site.ClientId, site.Id, req.AgentType, req.Rdp, req.Ping, req.Power, req.NatsPort, api, url, InstallerCommands.FileName(version, req.Arch), password);
        _ = Task.Run(() => RunJobAsync(run));      // not tied to the HTTP request: the request returns immediately
        return Outcome.Ok(ToDto(job));
    }

    private sealed record RunArgs(Job Job, int ClientId, int SiteId, string AgentType, bool Rdp, bool Ping, bool Power, int? NatsPort, string Api, string Url, string File, string Password);

    private async Task RunJobAsync(RunArgs a)
    {
        var job = a.Job;
        var o = opt.Value.Push;
        using var abort = new CancellationTokenSource();          // set when a credential is rejected
        try
        {
            using var gate = new SemaphoreSlim(Math.Clamp(o.MaxParallel, 1, 8));
            await Task.WhenAll(job.Targets.Select(async t =>
            {
                await gate.WaitAsync();
                try
                {
                    if (abort.IsCancellationRequested) { Set(job, t, PushInstall.Skipped, "Skipped because the credential was rejected by another computer (to avoid locking the account).", null); return; }
                    Set(job, t, PushInstall.Running, null, null);
                    var script = await BuildScriptAsync(a);
                    var r = await remote.RunAsync(new RemoteRunRequest(PushInstall.Connect(t.Host, o.DnsSuffix), job.Username, a.Password, script, o.UseSsl, Math.Max(60, o.TargetTimeoutSeconds)));
                    if (r.Category == PushInstall.CatAuth) abort.Cancel();
                    var msg = r.Message is null ? null : PushInstall.Scrub(r.Message, a.Password);
                    if (r.Status == PushInstall.Failed && r.Category != PushInstall.CatAuth && PushInstall.NameHint(t.Host, o.DnsSuffix) is { } hint) msg = $"{msg} — {hint}";
                    Set(job, t, r.Status, msg, r.Category);
                }
                catch (Exception e)
                {
                    log.LogWarning("Push to {Host} failed: {Type}", t.Host, e.GetType().Name);     // never log e.Message: it could carry arguments
                    Set(job, t, PushInstall.Failed, "Unexpected error on the server.", PushInstall.CatError);
                }
                finally { gate.Release(); }
            }));
        }
        finally
        {
            lock (job.Gate) { job.State = "done"; job.FinishedAt = DateTime.UtcNow; }
        }
    }

    /// <summary>A fresh one-hour installer token per computer, so a queued target never starts with a token that is about to expire.</summary>
    private async Task<string> BuildScriptAsync(RunArgs a)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installer = await db.Users.AsNoTracking().Where(u => u.IsInstallerUser && u.IsActive).OrderBy(u => u.Id).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("no installer account");
        var (token, _) = await scope.ServiceProvider.GetRequiredService<ITokenService>().CreateInstallerTokenAsync(installer.Id, TimeSpan.FromHours(1));
        var args = InstallerCommands.InstallArguments(a.Api, a.ClientId, a.SiteId, a.AgentType, token, a.Rdp, a.Ping, a.Power, a.NatsPort);
        return PushInstall.RemoteScript(a.Url, a.File, args, a.Job.Force);
    }

    private static void Set(Job j, Target t, string status, string? message, string? category)
    { lock (j.Gate) { t.Status = status; t.Message = message; t.Category = category; } }

    private void Prune()
    {
        foreach (var j in _jobs.Values.Where(j => j.FinishedAt is { } f && DateTime.UtcNow - f > Retention).ToList()) _jobs.TryRemove(j.Id, out _);
        foreach (var j in _jobs.Values.Where(j => j.FinishedAt is not null).OrderByDescending(j => j.CreatedAt).Skip(MaxJobsKept).ToList()) _jobs.TryRemove(j.Id, out _);
    }

    private static PushJobDto ToDto(Job j)
    {
        lock (j.Gate)
            return new PushJobDto(j.Id, j.State, j.CreatedAt, j.FinishedAt, j.ClientName, j.SiteName, j.Username, j.Force,
                j.Targets.Select(t => new PushTargetDto(t.Host, t.Status, t.Message, t.Category)).ToList(), j.Warnings);
    }

    private static bool CanView(User caller, Job j) => j.OwnerId == caller.Id || PermissionEvaluator.IsSuper(caller);

    public Task<Outcome<PushJobDto>> GetAsync(User caller, string id, CancellationToken ct = default) =>
        Task.FromResult(_jobs.TryGetValue(id, out var j) && CanView(caller, j) ? Outcome.Ok(ToDto(j)) : Outcome.NotFound<PushJobDto>("Push job not found (jobs are kept in memory for 6 hours)"));

    public Task<Outcome<IReadOnlyList<PushJobDto>>> ListAsync(User caller, CancellationToken ct = default) =>
        Task.FromResult(Outcome.Ok<IReadOnlyList<PushJobDto>>(_jobs.Values.Where(j => CanView(caller, j)).OrderByDescending(j => j.CreatedAt).Take(10).Select(ToDto).ToList()));
}
