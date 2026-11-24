using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>
/// The one place that decides which policy applies to an agent: agent, then its group, then site, then client
/// (see <see cref="PolicyChain"/>). Checks, tasks and patch settings all go through here.
/// </summary>
public static class EffectivePolicy
{
    /// <summary>Id of the policy that applies to this agent, or null.</summary>
    public static async Task<int?> IdForAsync(AppDbContext db, Agent agent, CancellationToken ct = default)
    {
        if (agent.BlockPolicyInheritance) return null;
        var site = await db.Sites.AsNoTracking().Where(s => s.Id == agent.SiteId)
            .Select(s => new { s.PolicyId, ClientPolicyId = s.Client.PolicyId }).FirstOrDefaultAsync(ct);
        var groupPolicyId = agent.GroupId is { } gid ? await db.AssetGroups.AsNoTracking().Where(g => g.Id == gid).Select(g => g.PolicyId).FirstOrDefaultAsync(ct) : null;
        var ids = new[] { agent.PolicyId, groupPolicyId, site?.PolicyId, site?.ClientPolicyId }
            .Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0) return null;
        var active = await db.Policies.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Active, ct);
        return PolicyChain.Pick(false, Ref(active, agent.PolicyId), Ref(active, groupPolicyId), Ref(active, site?.PolicyId), Ref(active, site?.ClientPolicyId));
    }

    /// <summary>The policy that applies to this agent, or null.</summary>
    public static async Task<Policy?> ForAsync(AppDbContext db, Agent agent, CancellationToken ct = default)
    {
        var id = await IdForAsync(db, agent, ct);
        return id is null ? null : await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    /// <summary>Effective policy id for every agent that has one (agent database id to policy id).</summary>
    public static async Task<Dictionary<int, int>> MapAsync(AppDbContext db, CancellationToken ct = default)
    {
        var agents = await db.Agents.AsNoTracking().Select(a => new
        {
            a.Id,
            a.PolicyId,
            a.BlockPolicyInheritance,
            GroupPolicyId = a.Group != null ? a.Group.PolicyId : null,
            SitePolicyId = a.Site.PolicyId,
            ClientPolicyId = a.Site.Client.PolicyId,
        }).ToListAsync(ct);
        var active = await db.Policies.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Active, ct);
        var map = new Dictionary<int, int>();
        foreach (var a in agents)
        {
            var id = PolicyChain.Pick(a.BlockPolicyInheritance, Ref(active, a.PolicyId), Ref(active, a.GroupPolicyId), Ref(active, a.SitePolicyId), Ref(active, a.ClientPolicyId));
            if (id is { } pid) map[a.Id] = pid;
        }
        return map;
    }

    /// <summary>Database ids of the agents the given policy currently applies to.</summary>
    public static async Task<List<int>> AgentIdsAsync(AppDbContext db, int policyId, CancellationToken ct = default)
    {
        var map = await MapAsync(db, ct);
        return map.Where(kv => kv.Value == policyId).Select(kv => kv.Key).ToList();
    }

    private static PolicyRef? Ref(Dictionary<int, bool> active, int? id) =>
        id is { } i && active.TryGetValue(i, out var on) ? new PolicyRef(i, on) : null;
}
