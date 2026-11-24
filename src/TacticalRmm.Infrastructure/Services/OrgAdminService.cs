using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class OrgAdminService(AppDbContext db, IAuditService audit) : IOrgAdminService
{
    private static string? CheckName(string? n) => string.IsNullOrWhiteSpace(n) || n.Trim().Length > 255 ? "name is required (max 255 characters)" : null;

    public async Task<Outcome<bool>> UpdateClientAsync(User caller, int id, ClientUpdate req, CancellationToken ct = default)
    {
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Outcome.NotFound<bool>();
        if (!ScopeGuard.CanSeeClient(caller, id)) return Outcome.Forbidden<bool>();
        var policyChange = req.ClearPolicy == true || req.Policy is not null;
        var templateChange = req.ClearAlertTemplate == true || req.AlertTemplate is not null;
        if (req.Name is null && !policyChange && !templateChange) return Outcome.Invalid<bool>("name, policy or alert template is required");

        string? oldName = null;
        if (req.Name is not null)
        {
            var err = CheckName(req.Name);
            if (err is not null) return Outcome.Invalid<bool>(err);
            var name = req.Name.Trim();
            if (await db.Clients.AnyAsync(x => x.Name == name && x.Id != id, ct)) return Outcome.Conflict<bool>("A client with this name already exists");
            oldName = c.Name; c.Name = name;
        }
        string? policyNote = null;
        if (policyChange)
        {
            // A policy runs scripts and tasks on every agent below, so it needs the policy permission, not just the client one.
            if (!PermissionEvaluator.Has(caller, Perm.ManagePolicies)) return Outcome.Forbidden<bool>("Assigning a policy needs the policy permission");
            if (req.ClearPolicy == true) { c.PolicyId = null; policyNote = "removed its policy"; }
            else
            {
                var pol = await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.Policy, ct);
                if (pol is null) return Outcome.NotFound<bool>("Policy not found");
                c.PolicyId = pol.Id; policyNote = $"set its policy to '{pol.Name}'";
            }
        }
        string? templateNote = null;
        if (templateChange)
        {
            if (!PermissionEvaluator.Has(caller, Perm.ManageAlerts)) return Outcome.Forbidden<bool>("Assigning an alert template needs the alerts permission");
            if (req.ClearAlertTemplate == true) { c.AlertTemplateId = null; templateNote = "removed its alert template"; }
            else
            {
                var tpl = await db.AlertTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.AlertTemplate, ct);
                if (tpl is null) return Outcome.NotFound<bool>("Alert template not found");
                c.AlertTemplateId = tpl.Id; templateNote = $"set its alert template to '{tpl.Name}'";
            }
        }
        await db.SaveChangesAsync(ct);
        var what = new[] { oldName is null ? null : $"renamed it from '{oldName}'", policyNote, templateNote }.Where(x => x is not null);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "client", $"{caller.Username} modified client '{c.Name}': {string.Join(", ", what)}", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<bool>> DeleteClientAsync(User caller, int id, CancellationToken ct = default)
    {
        var c = await db.Clients.Include(x => x.Sites).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Outcome.NotFound<bool>();
        if (!ScopeGuard.CanSeeClient(caller, id)) return Outcome.Forbidden<bool>();
        var siteIds = c.Sites.Select(s => s.Id).ToList();
        var agents = await db.Agents.CountAsync(a => siteIds.Contains(a.SiteId), ct);
        if (agents > 0) return Outcome.Conflict<bool>($"Client still has {agents} agent(s); move or uninstall them first");

        // Custom field values point to their client/site by id only, so they are removed here.
        db.CustomFieldValues.RemoveRange(db.CustomFieldValues.Where(v => (v.Field.Model == "client" && v.TargetId == id) || (v.Field.Model == "site" && siteIds.Contains(v.TargetId))));
        db.Sites.RemoveRange(c.Sites);
        db.Clients.Remove(c);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "client", $"{caller.Username} deleted client '{c.Name}'", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<SiteDto>> AddSiteAsync(User caller, SiteUpsert req, CancellationToken ct = default)
    {
        var err = CheckName(req.Name);
        if (err is not null) return Outcome.Invalid<SiteDto>(err);
        if (req.Client is not { } cid) return Outcome.Invalid<SiteDto>("client is required");
        if (!await db.Clients.AnyAsync(c => c.Id == cid, ct)) return Outcome.NotFound<SiteDto>("Client not found");
        if (!ScopeGuard.CanSeeClient(caller, cid)) return Outcome.Forbidden<SiteDto>();
        var name = req.Name!.Trim();
        if (await db.Sites.AnyAsync(s => s.ClientId == cid && s.Name == name, ct)) return Outcome.Conflict<SiteDto>("This client already has a site with that name");

        var site = new Site { Name = name, ClientId = cid };
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "site", $"{caller.Username} added site '{name}'", ct: ct);
        return Outcome.Ok(new SiteDto(site.Id, site.Name, site.ClientId));
    }

    public async Task<Outcome<SiteDto>> UpdateSiteAsync(User caller, int id, SiteUpsert req, CancellationToken ct = default)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (site is null) return Outcome.NotFound<SiteDto>();
        if (!PermissionEvaluator.CanSeeSite(caller, site)) return Outcome.Forbidden<SiteDto>();
        var newClient = req.Client ?? site.ClientId;
        if (newClient != site.ClientId)
        {
            if (!await db.Clients.AnyAsync(c => c.Id == newClient, ct)) return Outcome.NotFound<SiteDto>("Client not found");
            if (!ScopeGuard.CanSeeClient(caller, newClient)) return Outcome.Forbidden<SiteDto>();
        }
        var name = (req.Name ?? site.Name).Trim();
        var err = CheckName(name);
        if (err is not null) return Outcome.Invalid<SiteDto>(err);
        if (await db.Sites.AnyAsync(s => s.ClientId == newClient && s.Name == name && s.Id != id, ct)) return Outcome.Conflict<SiteDto>("This client already has a site with that name");

        string? policyNote = null;
        if (req.ClearPolicy == true || req.Policy is not null)
        {
            // A policy runs scripts and tasks on every agent below, so it needs the policy permission, not just the site one.
            if (!PermissionEvaluator.Has(caller, Perm.ManagePolicies)) return Outcome.Forbidden<SiteDto>("Assigning a policy needs the policy permission");
            if (req.ClearPolicy == true) { site.PolicyId = null; policyNote = "; removed its policy"; }
            else
            {
                var pol = await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.Policy, ct);
                if (pol is null) return Outcome.NotFound<SiteDto>("Policy not found");
                site.PolicyId = pol.Id; policyNote = $"; set its policy to '{pol.Name}'";
            }
        }
        if (req.ClearAlertTemplate == true || req.AlertTemplate is not null)
        {
            if (!PermissionEvaluator.Has(caller, Perm.ManageAlerts)) return Outcome.Forbidden<SiteDto>("Assigning an alert template needs the alerts permission");
            if (req.ClearAlertTemplate == true) { site.AlertTemplateId = null; policyNote += "; removed its alert template"; }
            else
            {
                var tpl = await db.AlertTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.AlertTemplate, ct);
                if (tpl is null) return Outcome.NotFound<SiteDto>("Alert template not found");
                site.AlertTemplateId = tpl.Id; policyNote += $"; set its alert template to '{tpl.Name}'";
            }
        }
        site.Name = name; site.ClientId = newClient;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "site", $"{caller.Username} modified site '{name}'{policyNote}", ct: ct);
        return Outcome.Ok(new SiteDto(site.Id, site.Name, site.ClientId));
    }

    public async Task<Outcome<bool>> DeleteSiteAsync(User caller, int id, CancellationToken ct = default)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (site is null) return Outcome.NotFound<bool>();
        if (!PermissionEvaluator.CanSeeSite(caller, site)) return Outcome.Forbidden<bool>();
        var agents = await db.Agents.CountAsync(a => a.SiteId == id, ct);
        if (agents > 0) return Outcome.Conflict<bool>($"Site still has {agents} agent(s); move or uninstall them first");
        if (await db.Sites.CountAsync(s => s.ClientId == site.ClientId, ct) <= 1) return Outcome.Conflict<bool>("A client must keep at least one site");

        db.CustomFieldValues.RemoveRange(db.CustomFieldValues.Where(v => v.Field.Model == "site" && v.TargetId == id));
        db.Sites.Remove(site);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "site", $"{caller.Username} deleted site '{site.Name}'", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<bool>> UpdateAgentAsync(User caller, string agentId, AgentUpdate req, CancellationToken ct = default)
    {
        var a = await db.Agents.Include(x => x.Site).FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (a is null) return Outcome.NotFound<bool>("Agent not found");
        if (!PermissionEvaluator.Has(caller, Perm.EditAgent)) return Outcome.Forbidden<bool>();
        if (!ScopeGuard.CanSeeAgent(caller, a)) return Outcome.Forbidden<bool>();

        if (req.Site is { } sid && sid != a.SiteId)
        {
            var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == sid, ct);
            if (site is null) return Outcome.NotFound<bool>("Site not found");
            if (!PermissionEvaluator.CanSeeSite(caller, site)) return Outcome.Forbidden<bool>("No access to the destination site");
            a.SiteId = sid; a.Site = site;
        }
        if (req.MonitoringType is not null)
        {
            if (req.MonitoringType is not (AgentMonType.Server or AgentMonType.Workstation)) return Outcome.Invalid<bool>("monitoring_type must be 'server' or 'workstation'");
            a.MonitoringType = req.MonitoringType;
        }
        if (req.CheckInterval is { } ci) { if (ci is < 15 or > 86400) return Outcome.Invalid<bool>("check_interval must be 15-86400 seconds"); a.CheckInterval = ci; }
        if (req.OfflineTime is { } ot) { if (ot is < 2 or > 10080) return Outcome.Invalid<bool>("offline_time must be 2-10080 minutes"); a.OfflineTime = ot; }
        if (req.OverdueTime is { } ov) { if (ov is < 3 or > 10080) return Outcome.Invalid<bool>("overdue_time must be 3-10080 minutes"); a.OverdueTime = ov; }
        if (req.Description is not null) a.Description = req.Description.Length > 255 ? req.Description[..255] : req.Description;
        if (req.MaintenanceMode is { } mm) a.MaintenanceMode = mm;
        if (req.OverdueEmailAlert is { } e) a.OverdueEmailAlert = e;
        if (req.OverdueTextAlert is { } t) a.OverdueTextAlert = t;
        if (req.OverdueDashboardAlert is { } d) a.OverdueDashboardAlert = d;
        if (req.BlockPolicyInheritance is { } bp) a.BlockPolicyInheritance = bp;
        if (req.TimeZone is not null)
        {
            if (req.TimeZone != "" && !TimeZoneInfo.TryFindSystemTimeZoneById(req.TimeZone, out _)) return Outcome.Invalid<bool>("Unknown time_zone");
            a.TimeZone = req.TimeZone == "" ? null : req.TimeZone;
        }
        if (req.ClearGroup == true || req.Group is not null)
        {
            if (!PermissionEvaluator.Has(caller, Perm.ManageGroups)) return Outcome.Forbidden<bool>("Changing the group needs the group permission");
            if (req.ClearGroup == true) a.GroupId = null;
            else
            {
                if (!await db.AssetGroups.AnyAsync(x => x.Id == req.Group, ct)) return Outcome.NotFound<bool>("Group not found");
                a.GroupId = req.Group;
            }
        }
        if (req.ClearPolicy == true) a.PolicyId = null;
        else if (req.Policy is { } pid)
        {
            if (!await db.Policies.AnyAsync(p => p.Id == pid, ct)) return Outcome.NotFound<bool>("Policy not found");
            a.PolicyId = pid;
        }

        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "agent", $"{caller.Username} edited agent {a.Hostname}", a.Hostname, a.Id, ct: ct);
        return Outcome.Ok(true);
    }
}
