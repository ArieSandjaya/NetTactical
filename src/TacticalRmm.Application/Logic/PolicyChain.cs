namespace TacticalRmm.Application.Logic;

/// <summary>A policy assigned at some level (agent, group, site or client) and whether that policy is switched on.</summary>
public readonly record struct PolicyRef(int Id, bool Active);

/// <summary>
/// Which single policy applies to an agent. The most specific level wins: agent, then its group, then site, then client.
/// A policy that is switched off is skipped, so the next level up can still apply.
/// "Block policy inheritance" on the agent switches off every level.
/// </summary>
public static class PolicyChain
{
    public static int? Pick(bool blocked, PolicyRef? agent, PolicyRef? group, PolicyRef? site, PolicyRef? client)
    {
        if (blocked) return null;
        if (agent is { Active: true }) return agent.Value.Id;
        if (group is { Active: true }) return group.Value.Id;
        if (site is { Active: true }) return site.Value.Id;
        if (client is { Active: true }) return client.Value.Id;
        return null;
    }

    /// <summary>The same without a group level.</summary>
    public static int? Pick(bool blocked, PolicyRef? agent, PolicyRef? site, PolicyRef? client) => Pick(blocked, agent, null, site, client);

    /// <summary>Which level the effective policy comes from: "agent", "group", "site", "client" or null. Same rules as <see cref="Pick(bool, PolicyRef?, PolicyRef?, PolicyRef?, PolicyRef?)"/>.</summary>
    public static string? Source(bool blocked, PolicyRef? agent, PolicyRef? group, PolicyRef? site, PolicyRef? client)
    {
        if (blocked) return null;
        if (agent is { Active: true }) return "agent";
        if (group is { Active: true }) return "group";
        if (site is { Active: true }) return "site";
        if (client is { Active: true }) return "client";
        return null;
    }

    public static string? Source(bool blocked, PolicyRef? agent, PolicyRef? site, PolicyRef? client) => Source(blocked, agent, null, site, client);
}
