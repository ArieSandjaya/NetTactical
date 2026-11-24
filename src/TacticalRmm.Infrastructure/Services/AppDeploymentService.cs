using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class AppDeploymentService(AppDbContext db, IPermissionService perms, IAuditService audit, IAgentLogService agentLog) : IAppDeploymentService
{
    private const int KeepListed = 30;
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    private static SoftwarePackageDto ToDto(SoftwarePackage p) =>
        new(p.Id, p.Name, p.InstallerType, p.Source, p.Sha256, p.SilentArgs, p.TimeoutSeconds, p.DetectName, p.Note, p.CreatedBy, p.CreatedAt);

    private static void Apply(SoftwarePackage p, SoftwarePackageRequest r)
    {
        p.Name = r.Name.Trim(); p.InstallerType = r.InstallerType; p.Source = r.Source.Trim();
        p.Sha256 = string.IsNullOrWhiteSpace(r.Sha256) ? null : r.Sha256.Trim().ToUpperInvariant();
        p.SilentArgs = string.IsNullOrWhiteSpace(r.SilentArgs) ? null : r.SilentArgs.Trim();
        p.TimeoutSeconds = r.TimeoutSeconds;
        p.DetectName = string.IsNullOrWhiteSpace(r.DetectName) ? null : r.DetectName.Trim();
        p.Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
    }

    public async Task<Outcome<IReadOnlyList<SoftwarePackageDto>>> ListPackagesAsync(CancellationToken ct = default) =>
        Outcome.Ok<IReadOnlyList<SoftwarePackageDto>>((await db.SoftwarePackages.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct)).Select(ToDto).ToList());

    public async Task<Outcome<SoftwarePackageDto>> CreatePackageAsync(User caller, SoftwarePackageRequest req, CancellationToken ct = default)
    {
        if (AppInstall.Validate(req) is { } bad) return Outcome.Invalid<SoftwarePackageDto>(bad);
        if (await db.SoftwarePackages.AnyAsync(p => p.Name == req.Name.Trim(), ct)) return Outcome.Conflict<SoftwarePackageDto>("A package with this name already exists.");
        var p = new SoftwarePackage { CreatedBy = caller.Username, CreatedAt = DateTime.UtcNow };
        Apply(p, req);
        db.SoftwarePackages.Add(p);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "software_package", $"{caller.Username} added the package {p.Name} ({p.InstallerType})", ct: ct);
        return Outcome.Ok(ToDto(p));
    }

    public async Task<Outcome<SoftwarePackageDto>> UpdatePackageAsync(User caller, int id, SoftwarePackageRequest req, CancellationToken ct = default)
    {
        if (AppInstall.Validate(req) is { } bad) return Outcome.Invalid<SoftwarePackageDto>(bad);
        var p = await db.SoftwarePackages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Outcome.NotFound<SoftwarePackageDto>("Package not found");
        if (await db.SoftwarePackages.AnyAsync(x => x.Name == req.Name.Trim() && x.Id != id, ct)) return Outcome.Conflict<SoftwarePackageDto>("A package with this name already exists.");
        Apply(p, req);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "software_package", $"{caller.Username} changed the package {p.Name}", ct: ct);
        return Outcome.Ok(ToDto(p));
    }

    public async Task<Outcome<bool>> DeletePackageAsync(User caller, int id, CancellationToken ct = default)
    {
        var p = await db.SoftwarePackages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Outcome.NotFound<bool>("Package not found");
        var active = await db.AppDeploymentTargets.AnyAsync(t => t.Deployment.PackageId == id && (t.Status == AppInstall.Pending || t.Status == AppInstall.Running), ct);
        if (active) return Outcome.Conflict<bool>("This package still has computers waiting or installing. Delete those deployments first.");
        db.SoftwarePackages.Remove(p);     // its finished deployments go with it
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "software_package", $"{caller.Username} deleted the package {p.Name}", ct: ct);
        return Outcome.Ok(true);
    }

    private static AppDeploymentDto ToDto(AppDeployment d, IReadOnlyCollection<AppDeploymentTarget> visible, DateTime now, bool withTargets)
    {
        int C(string s) => visible.Count(t => t.Status == s);
        return new AppDeploymentDto(d.Id, d.PackageId, d.Package.Name, d.Reinstall, d.CreatedAt, d.ExpiresAt, d.ExpiresAt <= now, d.CreatedBy, visible.Count,
            C(AppInstall.Pending), C(AppInstall.Running), C(AppInstall.Ok), C(AppInstall.Failed), C(AppInstall.Skipped), C(AppInstall.Expired),
            withTargets ? visible.OrderBy(t => t.Agent.Hostname, StringComparer.OrdinalIgnoreCase)
                .Select(t => new AppTargetDto(t.Agent.AgentId, t.Agent.Hostname, t.Agent.Site.Client.Name, t.Agent.Site.Name, t.Status, t.Message, t.Attempts, t.UpdatedAt)).ToList() : null);
    }

    private IQueryable<AppDeployment> Deployments() => db.AppDeployments.Include(d => d.Package).Include(d => d.Targets).ThenInclude(t => t.Agent).ThenInclude(a => a.Site).ThenInclude(s => s.Client);

    public async Task<Outcome<IReadOnlyList<AppDeploymentDto>>> ListAsync(User caller, CancellationToken ct = default)
    {
        var rows = await Deployments().AsNoTracking().OrderByDescending(d => d.CreatedAt).Take(KeepListed).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var list = new List<AppDeploymentDto>();
        foreach (var d in rows)
        {
            var visible = d.Targets.Where(t => PermissionEvaluator.CanSeeSite(caller, t.Agent.Site)).ToList();
            if (visible.Count > 0) list.Add(ToDto(d, visible, now, false));
        }
        return Outcome.Ok<IReadOnlyList<AppDeploymentDto>>(list);
    }

    public async Task<Outcome<AppDeploymentDto>> GetAsync(User caller, int id, CancellationToken ct = default)
    {
        var d = await Deployments().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        var visible = d?.Targets.Where(t => PermissionEvaluator.CanSeeSite(caller, t.Agent.Site)).ToList();
        return d is null || visible!.Count == 0 ? Outcome.NotFound<AppDeploymentDto>("Deployment not found") : Outcome.Ok(ToDto(d, visible, DateTime.UtcNow, true));
    }

    public async Task<Outcome<AppDeploymentDto>> CreateAsync(User caller, AppDeploymentRequest req, CancellationToken ct = default)
    {
        if (AppInstall.Validate(req) is { } bad) return Outcome.Invalid<AppDeploymentDto>(bad);
        var pkg = await db.SoftwarePackages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.PackageId, ct);
        if (pkg is null) return Outcome.NotFound<AppDeploymentDto>("Package not found");

        var ids = req.AgentIds ?? []; var siteIds = req.SiteIds ?? []; var clientIds = req.ClientIds ?? [];
        var found = await db.Agents.AsNoTracking().Where(a => a.Plat == AgentPlat.Windows && (ids.Contains(a.AgentId) || siteIds.Contains(a.SiteId) || clientIds.Contains(a.Site.ClientId)))
            .Select(a => new { a.Id, a.AgentId, a.Hostname }).ToListAsync(ct);
        var allowed = new List<(int Id, string AgentId, string Hostname)>();
        foreach (var a in found)
            if (await perms.HasPermissionOnAgentAsync(caller, a.AgentId, ct)) allowed.Add((a.Id, a.AgentId, a.Hostname));
        // An asset the caller named but may not touch is an error; assets reached through a client or site are just left out.
        if (ids.Any(i => found.Any(f => f.AgentId == i) && allowed.All(x => x.AgentId != i))) return Outcome.Forbidden<AppDeploymentDto>();
        if (allowed.Count == 0) return Outcome.Invalid<AppDeploymentDto>("No Windows assets matched your choice.");
        if (allowed.Count > AppInstall.MaxTargets) return Outcome.Invalid<AppDeploymentDto>($"At most {AppInstall.MaxTargets} assets in one deployment. Choose fewer clients or sites.");

        var now = DateTime.UtcNow;
        var d = new AppDeployment
        {
            PackageId = pkg.Id, Reinstall = req.Reinstall, ExpiresAt = now.AddDays(req.ExpiresDays), CreatedBy = caller.Username, CreatedAt = now,
            Targets = allowed.Select(a => new AppDeploymentTarget { AgentId = a.Id, Status = AppInstall.Pending, UpdatedAt = now }).ToList(),
        };
        db.AppDeployments.Add(d);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.BulkAction, "agent",
            $"{caller.Username} started a deployment of {pkg.Name}{(req.Reinstall ? " (reinstall)" : "")} to {allowed.Count} asset(s): {string.Join(", ", allowed.Select(a => a.Hostname).Take(30))}{(allowed.Count > 30 ? ", …" : "")}", ct: ct);
        return await GetAsync(caller, d.Id, ct);
    }

    public async Task<Outcome<AppDeploymentDto>> RetryFailedAsync(User caller, int id, CancellationToken ct = default)
    {
        var d = await Deployments().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return Outcome.NotFound<AppDeploymentDto>("Deployment not found");
        var mine = d.Targets.Where(t => t.Status is AppInstall.Failed or AppInstall.Expired && PermissionEvaluator.CanSeeSite(caller, t.Agent.Site)).ToList();
        if (mine.Count == 0) return Outcome.Conflict<AppDeploymentDto>("Nothing to retry: no computer failed or expired.");
        var now = DateTime.UtcNow;
        foreach (var t in mine) { t.Status = AppInstall.Pending; t.Message = "Waiting to be tried again."; t.Attempts = 0; t.UpdatedAt = now; }
        if (d.ExpiresAt < now.AddDays(1)) d.ExpiresAt = now.AddDays(1);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.BulkAction, "agent", $"{caller.Username} retried {mine.Count} asset(s) of the deployment of {d.Package.Name}", ct: ct);
        return await GetAsync(caller, id, ct);
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var d = await Deployments().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return Outcome.NotFound<bool>("Deployment not found");
        if (d.Targets.Any(t => !PermissionEvaluator.CanSeeSite(caller, t.Agent.Site))) return Outcome.Forbidden<bool>();
        db.AppDeployments.Remove(d);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "agent", $"{caller.Username} deleted the deployment of {d.Package.Name}", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<IReadOnlyList<AppWork>> ClaimDueAsync(int max, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // Waiting computers of a deployment that has run out of time are given up on.
        await db.AppDeploymentTargets.Where(t => t.Status == AppInstall.Pending && t.Deployment.ExpiresAt <= now)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, AppInstall.Expired).SetProperty(t => t.Message, "The deployment expired before this computer could be reached.").SetProperty(t => t.UpdatedAt, (DateTime?)now), ct);
        // A target stuck in "running" (the server restarted in the middle of it) goes back to waiting.
        var stuck = now.AddSeconds(-(AppInstall.MaxTimeout + 300));
        await db.AppDeploymentTargets.Where(t => t.Status == AppInstall.Running && t.UpdatedAt < stuck)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, AppInstall.Pending).SetProperty(t => t.Message, "Waiting: the earlier attempt was interrupted."), ct);

        var seen = now - OnlineWindow;
        var due = await db.AppDeploymentTargets.AsNoTracking()
            .Where(t => t.Status == AppInstall.Pending && t.Deployment.ExpiresAt > now && t.Agent.LastSeen != null && t.Agent.LastSeen > seen)
            .OrderBy(t => t.Id).Take(max)
            .Select(t => new { t.Id, t.Attempts, t.Agent.AgentId, t.Agent.Hostname, t.Deployment.Reinstall, t.Deployment.CreatedBy, Pkg = t.Deployment.Package })
            .ToListAsync(ct);

        var work = new List<AppWork>();
        foreach (var t in due)
        {
            // Only one runner can win the change from waiting to running.
            var won = await db.AppDeploymentTargets.Where(x => x.Id == t.Id && x.Status == AppInstall.Pending)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, AppInstall.Running).SetProperty(x => x.Attempts, x => x.Attempts + 1).SetProperty(x => x.Message, "Installing…").SetProperty(x => x.UpdatedAt, (DateTime?)now), ct);
            if (won != 1) continue;
            var script = AppInstall.Script(t.Pkg.InstallerType, t.Pkg.Source, t.Pkg.Sha256, t.Pkg.SilentArgs, t.Pkg.DetectName, t.Reinstall);
            work.Add(new AppWork(t.Id, t.AgentId, t.Hostname, script, t.Pkg.TimeoutSeconds, t.CreatedBy ?? "deployment", t.Pkg.Name, t.Attempts + 1));
            await agentLog.RecordCommandAsync(t.AgentId, t.CreatedBy ?? "deployment", $"[deploy] {t.Pkg.Name}{(t.Reinstall ? " (reinstall)" : "")} from {t.Pkg.Source}", ct);
        }
        return work;
    }

    public async Task FinishAsync(AppWork work, string? payload, string? error, CancellationToken ct = default)
    {
        var (status, message) = AppInstall.Interpret(payload, error, work.Attempts);
        var now = DateTime.UtcNow;
        await db.AppDeploymentTargets.Where(t => t.Id == work.TargetId)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, status).SetProperty(t => t.Message, message).SetProperty(t => t.UpdatedAt, (DateTime?)now), ct);
    }
}
