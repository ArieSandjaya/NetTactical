using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class AssetGroupService(AppDbContext db, IPermissionService perms, IAuditService audit) : IAssetGroupService
{
    private const int MaxName = 100, MaxDescription = 255, MaxPerCall = 500;
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    public async Task<Outcome<IReadOnlyList<AssetGroupDto>>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.AssetGroups.AsNoTracking().OrderBy(g => g.Name)
            .Select(g => new AssetGroupDto(g.Id, g.Name, g.Description, g.PolicyId, g.Policy != null ? g.Policy.Name : null, db.Agents.Count(a => a.GroupId == g.Id))).ToListAsync(ct);
        return Outcome.Ok<IReadOnlyList<AssetGroupDto>>(rows);
    }

    private async Task<Outcome<AssetGroupDetailDto>> DetailAsync(User caller, int id, CancellationToken ct)
    {
        var g = await db.AssetGroups.AsNoTracking().Include(x => x.Policy).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return Outcome.NotFound<AssetGroupDetailDto>("Group not found");
        var members = await db.Agents.AsNoTracking().Include(a => a.Site).ThenInclude(s => s.Client).Where(a => a.GroupId == id).ToListAsync(ct);
        var seen = DateTime.UtcNow - OnlineWindow;
        var items = members.Where(a => PermissionEvaluator.CanSeeSite(caller, a.Site)).OrderBy(a => a.Hostname, StringComparer.OrdinalIgnoreCase)
            .Select(a => new GroupMemberDto(a.AgentId, a.Hostname, a.Site.Client.Name, a.Site.Name, a.LastSeen > seen)).ToList();
        return Outcome.Ok(new AssetGroupDetailDto(g.Id, g.Name, g.Description, g.PolicyId, g.Policy?.Name, members.Count, items));
    }

    public Task<Outcome<AssetGroupDetailDto>> GetAsync(User caller, int id, CancellationToken ct = default) => DetailAsync(caller, id, ct);

    private static string? CheckName(string? n) => string.IsNullOrWhiteSpace(n) || n.Trim().Length > MaxName || n.Any(char.IsControl) ? $"Enter a name of up to {MaxName} characters." : null;
    private static string? CheckDescription(string? d) => d is { Length: > MaxDescription } || d?.Any(char.IsControl) == true ? $"The description is longer than {MaxDescription} characters or has control characters." : null;

    public async Task<Outcome<AssetGroupDetailDto>> CreateAsync(User caller, AssetGroupUpsert req, CancellationToken ct = default)
    {
        if (CheckName(req.Name) is { } ne) return Outcome.Invalid<AssetGroupDetailDto>(ne);
        if (CheckDescription(req.Description) is { } de) return Outcome.Invalid<AssetGroupDetailDto>(de);
        var name = req.Name!.Trim();
        if (await db.AssetGroups.AnyAsync(x => x.Name == name, ct)) return Outcome.Conflict<AssetGroupDetailDto>("A group with this name already exists.");
        var g = new AssetGroup { Name = name, Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(), CreatedBy = caller.Username, CreatedAt = DateTime.UtcNow };
        string? policyNote = null;
        if (req.Policy is not null)
        {
            var r = await ApplyPolicyAsync(caller, g, req.Policy, ct);
            if (r.Error is not null) return r.Error;
            policyNote = r.Note;
        }
        db.AssetGroups.Add(g);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "asset_group", $"{caller.Username} created the group '{g.Name}'{(policyNote is null ? "" : $" and {policyNote}")}", ct: ct);
        return await DetailAsync(caller, g.Id, ct);
    }

    public async Task<Outcome<AssetGroupDetailDto>> UpdateAsync(User caller, int id, AssetGroupUpsert req, CancellationToken ct = default)
    {
        var g = await db.AssetGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return Outcome.NotFound<AssetGroupDetailDto>("Group not found");
        var notes = new List<string>();
        if (req.Name is not null)
        {
            if (CheckName(req.Name) is { } ne) return Outcome.Invalid<AssetGroupDetailDto>(ne);
            var name = req.Name.Trim();
            if (await db.AssetGroups.AnyAsync(x => x.Name == name && x.Id != id, ct)) return Outcome.Conflict<AssetGroupDetailDto>("A group with this name already exists.");
            if (name != g.Name) { notes.Add($"renamed it from '{g.Name}'"); g.Name = name; }
        }
        if (req.Description is not null)
        {
            if (CheckDescription(req.Description) is { } de) return Outcome.Invalid<AssetGroupDetailDto>(de);
            g.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        }
        if (req.ClearPolicy == true || req.Policy is not null)
        {
            var r = await ApplyPolicyAsync(caller, g, req.ClearPolicy == true ? null : req.Policy, ct);
            if (r.Error is not null) return r.Error;
            notes.Add(r.Note!);
        }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "asset_group", $"{caller.Username} changed the group '{g.Name}'{(notes.Count == 0 ? "" : ": " + string.Join(", ", notes))}", ct: ct);
        return await DetailAsync(caller, id, ct);
    }

    /// <summary>A policy runs scripts and tasks on every member, so setting one needs the policy permission and not only the group one.</summary>
    private async Task<(Outcome<AssetGroupDetailDto>? Error, string? Note)> ApplyPolicyAsync(User caller, AssetGroup g, int? policyId, CancellationToken ct)
    {
        if (!PermissionEvaluator.Has(caller, Perm.ManagePolicies)) return (Outcome.Forbidden<AssetGroupDetailDto>("Assigning a policy needs the policy permission"), null);
        if (policyId is null) { g.PolicyId = null; return (null, "removed its policy"); }
        var pol = await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == policyId, ct);
        if (pol is null) return (Outcome.NotFound<AssetGroupDetailDto>("Policy not found"), null);
        g.PolicyId = pol.Id;
        return (null, $"set its policy to '{pol.Name}'");
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var g = await db.AssetGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return Outcome.NotFound<bool>("Group not found");
        var n = await db.Agents.CountAsync(a => a.GroupId == id, ct);
        db.AssetGroups.Remove(g);        // members are only released (their group becomes empty)
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "asset_group", $"{caller.Username} deleted the group '{g.Name}' ({n} member(s) released)", ct: ct);
        return Outcome.Ok(true);
    }

    private static string? CheckIds(GroupMembersRequest req)
    {
        var ids = req.AgentIds ?? [];
        if (ids.Count == 0) return "Choose at least one asset.";
        if (ids.Count > MaxPerCall) return $"At most {MaxPerCall} assets at once.";
        if (ids.Any(i => !AgentIdValidator.IsValid(i)) || ids.Distinct().Count() != ids.Count) return "An asset id is not valid or is listed twice.";
        return null;
    }

    public async Task<Outcome<AssetGroupDetailDto>> AddMembersAsync(User caller, int id, GroupMembersRequest req, CancellationToken ct = default)
    {
        if (CheckIds(req) is { } bad) return Outcome.Invalid<AssetGroupDetailDto>(bad);
        var g = await db.AssetGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return Outcome.NotFound<AssetGroupDetailDto>("Group not found");
        var ids = req.AgentIds!;
        var agents = await db.Agents.Where(a => ids.Contains(a.AgentId)).ToListAsync(ct);
        if (agents.Count != ids.Count) return Outcome.NotFound<AssetGroupDetailDto>("Some of the chosen assets no longer exist. Reload the page.");
        foreach (var a in agents)
            if (!await perms.HasPermissionOnAgentAsync(caller, a.AgentId, ct)) return Outcome.Forbidden<AssetGroupDetailDto>();
        var moved = agents.Count(a => a.GroupId is { } old && old != id);
        foreach (var a in agents) a.GroupId = id;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "asset_group",
            $"{caller.Username} added {agents.Count} asset(s) to the group '{g.Name}'{(moved > 0 ? $" ({moved} moved from another group)" : "")}: {string.Join(", ", agents.Select(a => a.Hostname).Take(30))}{(agents.Count > 30 ? ", …" : "")}", ct: ct);
        return await DetailAsync(caller, id, ct);
    }

    public async Task<Outcome<AssetGroupDetailDto>> RemoveMembersAsync(User caller, int id, GroupMembersRequest req, CancellationToken ct = default)
    {
        if (CheckIds(req) is { } bad) return Outcome.Invalid<AssetGroupDetailDto>(bad);
        var g = await db.AssetGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return Outcome.NotFound<AssetGroupDetailDto>("Group not found");
        var ids = req.AgentIds!;
        var members = await db.Agents.Where(a => a.GroupId == id && ids.Contains(a.AgentId)).ToListAsync(ct);
        foreach (var a in members)
            if (!await perms.HasPermissionOnAgentAsync(caller, a.AgentId, ct)) return Outcome.Forbidden<AssetGroupDetailDto>();
        foreach (var a in members) a.GroupId = null;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "asset_group", $"{caller.Username} removed {members.Count} asset(s) from the group '{g.Name}'", ct: ct);
        return await DetailAsync(caller, id, ct);
    }
}
