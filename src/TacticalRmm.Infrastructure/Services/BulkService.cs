using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>
/// Runs one action on several assets in the background (at most 8 at a time). Singleton: jobs live in memory for 6 hours and are lost on restart.
/// The caller needs the permission of the action and access to every chosen asset; the whole request is refused if one is not allowed.
/// </summary>
public sealed class BulkService(IServiceScopeFactory scopes, IAgentCommandBus bus, ILogger<BulkService> log) : IBulkService
{
    private const int MaxRunning = 3, MaxJobsKept = 40, Parallel = 8;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(6);

    private sealed class Target { public required string AgentId { get; init; } public required string Hostname { get; init; } public string Status { get; set; } = BulkRules.Queued; public string? Message { get; set; } }
    private sealed class Job
    {
        public required string Id { get; init; }
        public required int OwnerId { get; init; }
        public required string Username { get; init; }
        public required string Action { get; init; }
        public required DateTime CreatedAt { get; init; }
        public DateTime? FinishedAt { get; set; }
        public string State { get; set; } = "running";
        public required List<Target> Targets { get; init; }
        public readonly object Gate = new();
    }

    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    public async Task<Outcome<BulkJobDto>> StartAsync(User caller, BulkRequest req, CancellationToken ct = default)
    {
        if (BulkRules.Validate(req) is { } err) return Outcome.Invalid<BulkJobDto>(err);
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var perms = sp.GetRequiredService<IPermissionService>();
        if (!perms.HasPermission(caller, BulkRules.RequiredPermission(req.Action)!)) return Outcome.Forbidden<BulkJobDto>();

        var db = sp.GetRequiredService<AppDbContext>();
        var ids = req.AgentIds!;
        var agents = await db.Agents.AsNoTracking().Where(a => ids.Contains(a.AgentId)).Select(a => new { a.AgentId, a.Hostname }).ToListAsync(ct);
        if (agents.Count != ids.Count) return Outcome.NotFound<BulkJobDto>("Some of the selected assets no longer exist. Reload the page.");
        foreach (var a in agents)
            if (!await perms.HasPermissionOnAgentAsync(caller, a.AgentId, ct)) return Outcome.Forbidden<BulkJobDto>();
        if (req.Action == BulkRules.Script && await db.Scripts.FindAsync([req.Script], ct) is null) return Outcome.NotFound<BulkJobDto>("Script not found");

        Prune();
        if (_jobs.Values.Count(j => j.State == "running") >= MaxRunning) return Outcome.Conflict<BulkJobDto>("Three bulk actions are already running. Wait for one to finish.");

        var job = new Job
        {
            Id = Guid.NewGuid().ToString("N"), OwnerId = caller.Id, Username = caller.Username, Action = req.Action!, CreatedAt = DateTime.UtcNow,
            Targets = agents.OrderBy(a => a.Hostname, StringComparer.OrdinalIgnoreCase).Select(a => new Target { AgentId = a.AgentId, Hostname = a.Hostname }).ToList(),
        };
        _jobs[job.Id] = job;

        var detail = req.Action switch
        {
            BulkRules.Command => $"command: {BulkRules.Truncate(req.Cmd)}",
            BulkRules.Script => $"script id {req.Script}",
            BulkRules.MaintenanceOn => "maintenance mode on",
            BulkRules.MaintenanceOff => "maintenance mode off",
            _ => "restart",
        };
        await sp.GetRequiredService<IAuditService>().LogAsync(caller.Username, AuditAction.BulkAction, "agent",
            $"{caller.Username} started a bulk {req.Action} on {job.Targets.Count} asset(s) ({detail}): {string.Join(", ", job.Targets.Select(t => t.Hostname))}", ct: ct);

        _ = Task.Run(() => RunAsync(job, req));      // not tied to the HTTP request
        return Outcome.Ok(ToDto(job));
    }

    private async Task RunAsync(Job job, BulkRequest req)
    {
        try
        {
            using var gate = new SemaphoreSlim(Parallel);
            await Task.WhenAll(job.Targets.Select(async t =>
            {
                await gate.WaitAsync();
                try
                {
                    Set(job, t, BulkRules.Running, null);
                    var reply = await ExecuteAsync(job, req, t.AgentId);
                    var (status, msg) = BulkRules.FromReply(req.Action!, reply);
                    Set(job, t, status, msg);
                }
                catch (KeyNotFoundException) { Set(job, t, BulkRules.Failed, "Not found (asset or script was removed)."); }
                catch (Exception e)
                {
                    log.LogWarning(e, "Bulk {Action} failed for {Agent}", job.Action, t.Hostname);
                    Set(job, t, BulkRules.Failed, "Unexpected error on the server.");
                }
                finally { gate.Release(); }
            }));
        }
        finally { lock (job.Gate) { job.State = "done"; job.FinishedAt = DateTime.UtcNow; } }
    }

    private async Task<object?> ExecuteAsync(Job job, BulkRequest req, string agentId)
    {
        switch (req.Action)
        {
            case BulkRules.Command:
                return await bus.RequestAsync(agentId, new Dictionary<string, object?>
                    { ["func"] = "rawcmd", ["timeout"] = req.Timeout, ["payload"] = new Dictionary<string, object?> { ["command"] = req.Cmd, ["shell"] = req.Shell } },
                    TimeSpan.FromSeconds(req.Timeout + 2));
            case BulkRules.Script:
            {
                using var scope = scopes.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<IAgentService>()
                    .RunScriptAsync(agentId, new RunScriptRequest(req.Script, "wait", req.Args, req.RunAsUser, null, req.Timeout), job.Username);
            }
            case BulkRules.Reboot:
                return await bus.RequestAsync(agentId, new Dictionary<string, object?> { ["func"] = "rebootnow" }, TimeSpan.FromSeconds(10));
            case BulkRules.MaintenanceOn or BulkRules.MaintenanceOff:
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var n = await db.Agents.Where(a => a.AgentId == agentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.MaintenanceMode, req.Action == BulkRules.MaintenanceOn));
                return n == 1 ? "ok" : throw new KeyNotFoundException();
            }
            default:
                throw new InvalidOperationException("unknown action");
        }
    }

    private static void Set(Job j, Target t, string status, string? message) { lock (j.Gate) { t.Status = status; t.Message = message; } }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var j in _jobs.Values.Where(j => j.FinishedAt is { } f && f < cutoff)) _jobs.TryRemove(j.Id, out _);
        foreach (var j in _jobs.Values.Where(j => j.State == "done").OrderBy(j => j.CreatedAt).Take(Math.Max(0, _jobs.Count - MaxJobsKept))) _jobs.TryRemove(j.Id, out _);
    }

    private static BulkJobDto ToDto(Job j)
    {
        lock (j.Gate) return new BulkJobDto(j.Id, j.Action, j.State, j.CreatedAt, j.FinishedAt, j.Username, j.Targets.Select(t => new BulkTargetDto(t.AgentId, t.Hostname, t.Status, t.Message)).ToList());
    }

    public Task<Outcome<BulkJobDto>> GetAsync(User caller, string id, CancellationToken ct = default) =>
        Task.FromResult(_jobs.TryGetValue(id, out var j) && (j.OwnerId == caller.Id || PermissionEvaluator.IsSuper(caller))
            ? Outcome.Ok(ToDto(j)) : Outcome.NotFound<BulkJobDto>("Bulk action not found (jobs are kept in memory for 6 hours)"));
}
