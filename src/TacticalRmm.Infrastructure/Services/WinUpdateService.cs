using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Infrastructure.Persistence;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Infrastructure.Services;

public sealed class WinUpdateService(AppDbContext db, IAgentCommandBus bus, IAuditService audit) : IWinUpdateService
{
    public async Task SetRebootStateAsync(Agent agent, bool needsReboot, CancellationToken ct = default)
    {
        var tracked = await db.Agents.FirstAsync(a => a.Id == agent.Id, ct);
        tracked.NeedsReboot = needsReboot;
        await db.SaveChangesAsync(ct);

        var effective = await EffectiveRebootPolicyAsync(tracked, ct);
        if (PatchPolicyResolver.ShouldReboot(effective, needsReboot))
        {
            await bus.PublishAsync(agent.AgentId, new Dictionary<string, object?> { ["func"] = "rebootnow" }, ct);
            await audit.LogAsync("system", "execute_command", "agent",
                $"{agent.Hostname} is rebooting after updates were installed.", agent.Hostname, agent.Id, ct: ct);
        }
        await DeleteSupersededAsync(agent.Id, ct);
    }

    private async Task<string> EffectiveRebootPolicyAsync(Agent agent, CancellationToken ct)
    {
        var own = await db.WinUpdatePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.AgentId == agent.Id, ct);
        var agentValue = own?.RebootAfterInstall ?? "inherit";
        if (agentValue != "inherit" || agent.PolicyId is null || agent.BlockPolicyInheritance) return agentValue;

        var active = await db.Policies.AsNoTracking().AnyAsync(p => p.Id == agent.PolicyId && p.Active, ct);
        if (!active) return agentValue;
        var pol = await db.WinUpdatePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.PolicyId == agent.PolicyId, ct);
        return PatchPolicyResolver.EffectiveReboot(agentValue, pol?.RebootAfterInstall);
    }

    public async Task<bool> MarkResultAsync(Agent agent, string guid, bool success, CancellationToken ct = default)
    {
        var u = await db.WinUpdates.Where(x => x.AgentId == agent.Id && x.Guid == guid).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (u is null) return false;
        if (success) { u.Result = "success"; u.Downloaded = true; u.Installed = true; u.DateInstalled = DateTime.UtcNow; }
        else u.Result = "failed";
        await db.SaveChangesAsync(ct);
        await DeleteSupersededAsync(agent.Id, ct);
        return true;
    }

    public async Task<bool> SyncAsync(Agent agent, IReadOnlyList<WinUpdateItem> updates, CancellationToken ct = default)
    {
        if (updates.Count == 0) return false;

        var existing = await db.WinUpdates.Where(x => x.AgentId == agent.Id).ToListAsync(ct);
        var returned = new HashSet<string>();
        foreach (var item in updates)
        {
            returned.Add(item.Guid);
            var u = existing.Where(x => x.Guid == item.Guid).MaxBy(x => x.Id);
            if (u is not null) { u.Downloaded = item.Downloaded; u.Installed = item.Installed; continue; }

            if (item.KbArticleIds is not { Count: > 0 }) continue; // no KB: skip (same as Django)
            db.WinUpdates.Add(new WinUpdate
            {
                AgentId = agent.Id, Guid = item.Guid, Kb = "KB" + item.KbArticleIds[0], Title = item.Title,
                Installed = item.Installed, Downloaded = item.Downloaded, Description = item.Description,
                Severity = item.Severity, Categories = item.Categories ?? new(), CategoryIds = item.CategoryIds ?? new(),
                KbArticleIds = item.KbArticleIds, MoreInfoUrls = item.MoreInfoUrls ?? new(),
                SupportUrl = item.SupportUrl, RevisionNumber = item.RevisionNumber
            });
        }

        // updates that are not installed and no longer reported by the agent are deleted
        db.WinUpdates.RemoveRange(existing.Where(x => !x.Installed && (x.Guid is null || !returned.Contains(x.Guid))));
        await db.SaveChangesAsync(ct);
        await DeleteSupersededAsync(agent.Id, ct);
        return true;
    }

    public async Task RemoveByGuidAsync(Agent agent, string guid, CancellationToken ct = default)
    {
        var rows = await db.WinUpdates.Where(x => x.AgentId == agent.Id && x.Guid == guid).ToListAsync(ct);
        db.WinUpdates.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }

    private async Task DeleteSupersededAsync(int agentId, CancellationToken ct)
    {
        try
        {
            var rows = await db.WinUpdates.AsNoTracking().Where(x => x.AgentId == agentId)
                .Select(x => new { x.Id, x.Kb, x.Title }).ToListAsync(ct);
            var ids = SupersededUpdates.FindObsolete(rows.Select(r => (r.Id, r.Kb, r.Title)));
            if (ids.Count == 0) return;
            await db.WinUpdates.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* best-effort, same as suppress(Exception) in Django */ }
    }
}
