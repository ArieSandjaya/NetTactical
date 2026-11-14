using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>Loads the agent-level and policy-level patch settings and resolves them (agent over active policy, unless inheritance is blocked).</summary>
public static class PatchPolicyLoader
{
    public static async Task<(WinUpdatePolicy? Own, WinUpdatePolicy? Parent)> LoadAsync(AppDbContext db, Agent agent, CancellationToken ct)
    {
        var own = await db.WinUpdatePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.AgentId == agent.Id, ct);
        WinUpdatePolicy? parent = null;
        if (agent.PolicyId is not null && !agent.BlockPolicyInheritance && await db.Policies.AsNoTracking().AnyAsync(p => p.Id == agent.PolicyId && p.Active, ct))
            parent = await db.WinUpdatePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.PolicyId == agent.PolicyId, ct);
        return (own, parent);
    }

    public static async Task<EffectivePatch> EffectiveAsync(AppDbContext db, Agent agent, CancellationToken ct)
    {
        var (own, parent) = await LoadAsync(db, agent, ct);
        return WinUpdateRules.Resolve(own, parent);
    }
}
