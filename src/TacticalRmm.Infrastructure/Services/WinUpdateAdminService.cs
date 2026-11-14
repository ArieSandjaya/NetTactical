using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>Per-agent update list, approve/ignore, scan and install now, and the patch policy (agent and automation-policy level).</summary>
public sealed class WinUpdateAdminService(AppDbContext db, IAgentCommandBus bus, IAuditService audit) : IWinUpdateAdminService
{
    private static WinUpdatePolicyDto ToDto(WinUpdatePolicy? p) => p is null ? new WinUpdatePolicyDto() : new WinUpdatePolicyDto
    {
        Critical = p.Critical, Important = p.Important, Moderate = p.Moderate, Low = p.Low, Other = p.Other, RunTimeFrequency = p.RunTimeFrequency,
        RunTimeHour = p.RunTimeHour, RunTimeWeekdays = p.RunTimeWeekdays, RunTimeDay = p.RunTimeDay, RebootAfterInstall = p.RebootAfterInstall, LastRunAt = p.LastRunAt,
    };
    private static EffectivePatchDto ToDto(EffectivePatch e) => new(e.Critical, e.Important, e.Moderate, e.Low, e.Other, e.Frequency, e.Hour, e.Weekdays, e.Day, e.Reboot);

    private static string? Check(WinUpdatePolicyDto r) =>
        WinUpdateRules.Validate(r.Critical, r.Important, r.Moderate, r.Low, r.Other, r.RunTimeFrequency, r.RunTimeHour, r.RunTimeWeekdays, r.RunTimeDay, r.RebootAfterInstall);

    private static void Apply(WinUpdatePolicy p, WinUpdatePolicyDto r)
    {
        p.Critical = r.Critical; p.Important = r.Important; p.Moderate = r.Moderate; p.Low = r.Low; p.Other = r.Other;
        p.RunTimeFrequency = r.RunTimeFrequency; p.RunTimeHour = r.RunTimeHour; p.RunTimeWeekdays = r.RunTimeWeekdays; p.RunTimeDay = r.RunTimeDay; p.RebootAfterInstall = r.RebootAfterInstall;
    }

    private Task<Agent?> FindAgent(string agentId, CancellationToken ct) => db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.AgentId == agentId, ct);

    public async Task<Outcome<IReadOnlyList<WinUpdateDto>>> ListAsync(string agentId, CancellationToken ct = default)
    {
        var a = await FindAgent(agentId, ct);
        if (a is null) return Outcome.NotFound<IReadOnlyList<WinUpdateDto>>("Agent not found");
        var rows = await db.WinUpdates.AsNoTracking().Where(u => u.AgentId == a.Id).ToListAsync(ct);
        return Outcome.Ok<IReadOnlyList<WinUpdateDto>>(rows.OrderBy(u => u.Installed).ThenBy(u => PatchRules.SeverityRank(u.Severity)).ThenBy(u => u.Title)
            .Select(u => new WinUpdateDto(u.Guid ?? "", u.Kb, u.Title, u.Severity, u.Action, u.Result, u.Installed, u.Downloaded, u.DateInstalled)).ToList());
    }

    public async Task<Outcome<int>> SetActionAsync(User caller, string agentId, WinUpdateActionRequest req, CancellationToken ct = default)
    {
        if (!WinUpdateRules.Actions.Contains(req.Action)) return Outcome.Invalid<int>("action must be approve, ignore or nothing");
        if (req.Guids.Count is 0 or > 500) return Outcome.Invalid<int>("select between 1 and 500 updates");
        var a = await FindAgent(agentId, ct);
        if (a is null) return Outcome.NotFound<int>("Agent not found");
        var rows = await db.WinUpdates.Where(u => u.AgentId == a.Id && u.Guid != null && req.Guids.Contains(u.Guid) && !u.Installed).ToListAsync(ct);
        foreach (var r in rows) r.Action = req.Action;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "agent", $"{caller.Username} set {rows.Count} Windows update(s) on {a.Hostname} to '{req.Action}'", a.Hostname, a.Id, ct: ct);
        return Outcome.Ok(rows.Count);
    }

    public async Task<Outcome<bool>> ScanAsync(User caller, string agentId, CancellationToken ct = default)
    {
        var a = await FindAgent(agentId, ct);
        if (a is null) return Outcome.NotFound<bool>("Agent not found");
        await bus.PublishAsync(a.AgentId, new Dictionary<string, object?> { ["func"] = "getwinupdates" }, ct);
        await audit.LogAsync(caller.Username, AuditAction.ExecCommand, "agent", $"{caller.Username} requested a Windows Update scan on {a.Hostname}", a.Hostname, a.Id, ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<WinUpdateInstallResult>> InstallNowAsync(User caller, string agentId, CancellationToken ct = default)
    {
        var a = await FindAgent(agentId, ct);
        if (a is null) return Outcome.NotFound<WinUpdateInstallResult>("Agent not found");
        var r = await InstallApprovedAsync(a, ct);
        if (r.Requested > 0 && r.AgentOnline)
            await audit.LogAsync(caller.Username, AuditAction.ExecCommand, "agent", $"{caller.Username} started installing {r.Requested} approved Windows update(s) on {a.Hostname}", a.Hostname, a.Id, ct: ct);
        return Outcome.Ok(r);
    }

    /// <summary>Sends the approved, not yet installed updates to the agent. Does nothing (AgentOnline=false) when the agent does not answer a ping.</summary>
    public async Task<WinUpdateInstallResult> InstallApprovedAsync(Agent a, CancellationToken ct)
    {
        var guids = await db.WinUpdates.AsNoTracking().Where(u => u.AgentId == a.Id && u.Action == "approve" && !u.Installed && u.Guid != null).Select(u => u.Guid!).ToListAsync(ct);
        if (guids.Count == 0) return new WinUpdateInstallResult(0, true);
        var pong = await bus.RequestAsync(a.AgentId, new Dictionary<string, object?> { ["func"] = "ping" }, TimeSpan.FromSeconds(3), ct);
        if (pong as string != "pong") return new WinUpdateInstallResult(guids.Count, false);
        await bus.PublishAsync(a.AgentId, new Dictionary<string, object?> { ["func"] = "installwinupdates", ["guids"] = guids }, ct);
        return new WinUpdateInstallResult(guids.Count, true);
    }

    // ------------------------------------------------------------------ patch policy
    public async Task<Outcome<WinUpdatePolicyView>> GetAgentPolicyAsync(string agentId, CancellationToken ct = default)
    {
        var a = await FindAgent(agentId, ct);
        if (a is null) return Outcome.NotFound<WinUpdatePolicyView>("Agent not found");
        var (own, parent) = await PatchPolicyLoader.LoadAsync(db, a, ct);
        return Outcome.Ok(new WinUpdatePolicyView(ToDto(own), parent is null ? null : ToDto(parent), ToDto(WinUpdateRules.Resolve(own, parent))));
    }

    public async Task<Outcome<WinUpdatePolicyView>> SaveAgentPolicyAsync(User caller, string agentId, WinUpdatePolicyDto req, CancellationToken ct = default)
    {
        if (Check(req) is { } err) return Outcome.Invalid<WinUpdatePolicyView>(err);
        var a = await FindAgent(agentId, ct);
        if (a is null) return Outcome.NotFound<WinUpdatePolicyView>("Agent not found");
        var row = await db.WinUpdatePolicies.FirstOrDefaultAsync(p => p.AgentId == a.Id, ct);
        if (row is null) { row = new WinUpdatePolicy { AgentId = a.Id }; db.WinUpdatePolicies.Add(row); }
        Apply(row, req);
        await db.SaveChangesAsync(ct);
        await ReapplyApprovalsAsync([a.Id], ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "agent", $"{caller.Username} changed the Windows Update settings of {a.Hostname}", a.Hostname, a.Id, ct: ct);
        return await GetAgentPolicyAsync(agentId, ct);
    }

    public async Task<Outcome<WinUpdatePolicyView>> GetPolicyAsync(int policyId, CancellationToken ct = default)
    {
        if (!await db.Policies.AnyAsync(p => p.Id == policyId, ct)) return Outcome.NotFound<WinUpdatePolicyView>("Policy not found");
        var row = await db.WinUpdatePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.PolicyId == policyId, ct);
        return Outcome.Ok(new WinUpdatePolicyView(ToDto(row), null, ToDto(WinUpdateRules.Resolve(null, row))));
    }

    public async Task<Outcome<WinUpdatePolicyView>> SavePolicyAsync(User caller, int policyId, WinUpdatePolicyDto req, CancellationToken ct = default)
    {
        if (Check(req) is { } err) return Outcome.Invalid<WinUpdatePolicyView>(err);
        var pol = await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == policyId, ct);
        if (pol is null) return Outcome.NotFound<WinUpdatePolicyView>("Policy not found");
        var row = await db.WinUpdatePolicies.FirstOrDefaultAsync(p => p.PolicyId == policyId, ct);
        if (row is null) { row = new WinUpdatePolicy { PolicyId = policyId }; db.WinUpdatePolicies.Add(row); }
        Apply(row, req);
        await db.SaveChangesAsync(ct);
        var ids = await db.Agents.AsNoTracking().Where(a => a.PolicyId == policyId && !a.BlockPolicyInheritance).Select(a => a.Id).ToListAsync(ct);
        await ReapplyApprovalsAsync(ids, ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "policy", $"{caller.Username} changed the Windows Update settings of policy {pol.Name}", pol.Name, ct: ct);
        return await GetPolicyAsync(policyId, ct);
    }

    /// <summary>After a settings change, updates that nobody decided on yet ("nothing") get the new automatic decision. Manual approve/ignore choices are never overwritten.</summary>
    private async Task ReapplyApprovalsAsync(IReadOnlyCollection<int> agentIds, CancellationToken ct)
    {
        foreach (var id in agentIds.Take(2000))
        {
            var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (agent is null) continue;
            var eff = await PatchPolicyLoader.EffectiveAsync(db, agent, ct);
            var pending = await db.WinUpdates.Where(u => u.AgentId == id && !u.Installed && u.Action == "nothing").ToListAsync(ct);
            foreach (var u in pending) u.Action = WinUpdateRules.AutoAction(eff, u.Severity);
        }
        await db.SaveChangesAsync(ct);
    }
}
