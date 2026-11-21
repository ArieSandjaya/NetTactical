using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class DeploymentService(AppDbContext db, ITokenService tokens, IAuditService audit, IOptions<TacticalOptions> opt) : IDeploymentService
{
    /// <summary>The enrolment token inside a served script only has to survive the install itself.</summary>
    private static readonly TimeSpan TokenLife = TimeSpan.FromHours(2);

    private static DeploymentDto ToDto(Deployment d, DateTime now)
    {
        var link = DeploymentRules.Link(d.Api, d.Uid);
        return new DeploymentDto(d.Id, d.Site.Client.Name, d.Site.Name, d.AgentType, d.Arch, d.Api, link, DeploymentRules.Command(link), d.ExpiresAt,
            DeploymentRules.Expired(d.ExpiresAt, now), d.Uses, d.MaxUses, d.Rdp, d.Ping, d.Power, d.NatsPort, d.Note, d.CreatedBy, d.CreatedAt);
    }

    public async Task<Outcome<IReadOnlyList<DeploymentDto>>> ListAsync(User caller, CancellationToken ct = default)
    {
        var rows = await db.Deployments.AsNoTracking().Include(d => d.Site).ThenInclude(s => s.Client).OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
        var now = DateTime.UtcNow;
        return Outcome.Ok<IReadOnlyList<DeploymentDto>>(rows.Where(d => PermissionEvaluator.CanSeeSite(caller, d.Site)).Select(d => ToDto(d, now)).ToList());
    }

    public async Task<Outcome<DeploymentDto>> CreateAsync(User caller, DeploymentRequest req, string defaultApi, CancellationToken ct = default)
    {
        var api = InstallerCommands.NormalizeApi(string.IsNullOrWhiteSpace(req.Api) ? defaultApi : req.Api);
        if (DeploymentRules.Validate(req, api) is { } bad) return Outcome.Invalid<DeploymentDto>(bad);
        var site = await db.Sites.Include(s => s.Client).FirstOrDefaultAsync(s => s.Id == req.SiteId && s.ClientId == req.ClientId, ct);
        if (site is null) return Outcome.NotFound<DeploymentDto>("Client or site not found");
        if (!PermissionEvaluator.CanSeeSite(caller, site)) return Outcome.Forbidden<DeploymentDto>();
        if (InstallerCommands.DownloadUrl(opt.Value.LatestAgentVer, req.Arch) is null) return Outcome.Conflict<DeploymentDto>($"Configured agent version '{opt.Value.LatestAgentVer}' is not valid.");

        var now = DateTime.UtcNow;
        var d = new Deployment
        {
            Uid = DeploymentRules.NewUid(), Site = site, SiteId = site.Id, AgentType = req.AgentType, Arch = req.Arch, Rdp = req.Rdp, Ping = req.Ping, Power = req.Power,
            NatsPort = req.NatsPort, Api = api, ExpiresAt = now.AddHours(req.ExpiresHours), MaxUses = req.MaxUses, Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            CreatedBy = caller.Username, CreatedAt = now,
        };
        db.Deployments.Add(d);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.AgentInstall, "agent",
            $"{caller.Username} created a deployment link for {site.Client.Name} / {site.Name} ({req.AgentType}, {req.Arch}, valid {req.ExpiresHours}h{(req.MaxUses is { } m ? $", max {m} uses" : "")})", ct: ct);
        return Outcome.Ok(ToDto(d, now));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var d = await db.Deployments.Include(x => x.Site).ThenInclude(s => s.Client).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return Outcome.NotFound<bool>("Deployment not found");
        if (!PermissionEvaluator.CanSeeSite(caller, d.Site)) return Outcome.Forbidden<bool>();
        db.Deployments.Remove(d);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.AgentInstall, "agent", $"{caller.Username} deleted the deployment link for {d.Site.Client.Name} / {d.Site.Name}", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<string>> ScriptAsync(string uid, CancellationToken ct = default)
    {
        const string gone = "This link is not valid.";
        if (!DeploymentRules.ValidUid(uid)) return Outcome.NotFound<string>(gone);
        var d = await db.Deployments.AsNoTracking().Include(x => x.Site).ThenInclude(s => s.Client).FirstOrDefaultAsync(x => x.Uid == uid, ct);
        if (d is null) return Outcome.NotFound<string>(gone);
        var installer = await db.Users.AsNoTracking().Where(u => u.IsInstallerUser && u.IsActive).OrderBy(u => u.Id).FirstOrDefaultAsync(ct);
        if (installer is null) return Outcome.NotFound<string>(gone);

        // Counting the use and checking expiry and the limit is one statement, so two computers cannot both take the last use.
        var now = DateTime.UtcNow;
        var taken = await db.Deployments.Where(x => x.Id == d.Id && x.ExpiresAt > now && (x.MaxUses == null || x.Uses < x.MaxUses))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Uses, x => x.Uses + 1), ct);
        if (taken != 1) return Outcome.NotFound<string>(gone);

        var version = opt.Value.LatestAgentVer;
        var url = InstallerCommands.DownloadUrl(version, d.Arch);
        if (url is null) return Outcome.NotFound<string>(gone);
        var (token, _) = await tokens.CreateInstallerTokenAsync(installer.Id, TokenLife, ct);
        var args = InstallerCommands.InstallArguments(d.Api, d.Site.ClientId, d.SiteId, d.AgentType, token, d.Rdp, d.Ping, d.Power, d.NatsPort);
        await audit.LogAsync("deployment", AuditAction.AgentInstall, "agent", $"Deployment link of {d.Site.Client.Name} / {d.Site.Name} was opened (use {d.Uses + 1}{(d.MaxUses is { } m ? $" of {m}" : "")})", ct: ct);
        return Outcome.Ok(DeploymentRules.Script(d.Site.Client.Name, d.Site.Name, url, InstallerCommands.FileName(version, d.Arch), args));
    }
}
