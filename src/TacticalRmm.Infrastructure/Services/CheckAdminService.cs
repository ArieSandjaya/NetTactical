using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class CheckAdminService(AppDbContext db, ICheckService checks, IAuditService audit) : ICheckAdminService
{
    public async Task<Outcome<IReadOnlyList<CheckDto>>> ListForAgentAsync(User caller, string agentId, CancellationToken ct = default)
    {
        var agent = await db.Agents.AsNoTracking().Include(a => a.Site).FirstOrDefaultAsync(a => a.AgentId == agentId, ct);
        if (agent is null) return Outcome.NotFound<IReadOnlyList<CheckDto>>("Agent not found");
        if (!ScopeGuard.CanSeeAgent(caller, agent)) return Outcome.Forbidden<IReadOnlyList<CheckDto>>();

        var list = await checks.GetChecksForAgentAsync(agent, ct);
        var ids = list.Select(c => c.Id).ToList();
        var results = await db.CheckResults.AsNoTracking().Where(r => r.AgentId == agent.Id && ids.Contains(r.CheckId))
            .ToDictionaryAsync(r => r.CheckId, ct);
        IReadOnlyList<CheckDto> dtos = list.Select(c =>
            CheckDto.From(c, c.AgentId is null ? null : agent.AgentId, results.GetValueOrDefault(c.Id), c.PolicyId is not null)).ToList();
        return Outcome.Ok(dtos);
    }

    public async Task<Outcome<IReadOnlyList<CheckDto>>> ListForPolicyAsync(User caller, int policyId, CancellationToken ct = default)
    {
        if (!await db.Policies.AnyAsync(p => p.Id == policyId, ct)) return Outcome.NotFound<IReadOnlyList<CheckDto>>("Policy not found");
        var list = await db.Checks.AsNoTracking().Where(c => c.PolicyId == policyId).OrderBy(c => c.Id).ToListAsync(ct);
        IReadOnlyList<CheckDto> dtos = list.Select(c => CheckDto.From(c, null, null, false)).ToList();
        return Outcome.Ok(dtos);
    }

    public async Task<Outcome<CheckDto>> GetAsync(User caller, int id, CancellationToken ct = default)
    {
        var c = await db.Checks.AsNoTracking().Include(x => x.Agent).ThenInclude(a => a!.Site).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Outcome.NotFound<CheckDto>();
        if (c.Agent is not null && !ScopeGuard.CanSeeAgent(caller, c.Agent)) return Outcome.Forbidden<CheckDto>();
        var r = c.AgentId is null ? null : await db.CheckResults.AsNoTracking().FirstOrDefaultAsync(x => x.CheckId == id && x.AgentId == c.AgentId, ct);
        return Outcome.Ok(CheckDto.From(c, c.Agent?.AgentId, r, false));
    }

    public async Task<Outcome<CheckDto>> CreateAsync(User caller, CheckUpsert req, CancellationToken ct = default)
    {
        if ((req.Agent is null) == (req.Policy is null)) return Outcome.Invalid<CheckDto>("Specify exactly one of 'agent' or 'policy'");
        var type = req.CheckType ?? "";
        if (!CheckValidator.IsKnownType(type)) return Outcome.Invalid<CheckDto>($"Unknown check_type '{type}'");

        Agent? agent = null;
        if (req.Agent is not null)
        {
            agent = await db.Agents.Include(a => a.Site).FirstOrDefaultAsync(a => a.AgentId == req.Agent, ct);
            if (agent is null) return Outcome.NotFound<CheckDto>("Agent not found");
            if (!ScopeGuard.CanSeeAgent(caller, agent)) return Outcome.Forbidden<CheckDto>();
        }
        else if (!await db.Policies.AnyAsync(p => p.Id == req.Policy, ct)) return Outcome.NotFound<CheckDto>("Policy not found");

        var check = new Check { CheckType = type, AgentId = agent?.Id, PolicyId = req.Policy };
        Apply(check, req);

        var err = CheckValidator.Validate(type, Snapshot(check)) ?? await ValidateScriptAsync(check, ct);
        if (err is not null) return Outcome.Invalid<CheckDto>(err);
        if ((check.Name?.Length ?? 0) > 255) return Outcome.Invalid<CheckDto>("name is too long (max 255)");

        if (string.IsNullOrWhiteSpace(check.Name))
        {
            var scriptName = check.ScriptId is null ? null : (await db.Scripts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == check.ScriptId, ct))?.Name;
            check.Name = CheckValidator.DefaultName(type, Snapshot(check), scriptName);
        }

        db.Checks.Add(check);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "check", $"{caller.Username} added {type} check '{check.Name}'" +
            (agent is null ? $" to policy {check.PolicyId}" : $" on {agent.Hostname}"), agent?.Hostname, agent?.Id, ct: ct);
        return Outcome.Ok(CheckDto.From(check, agent?.AgentId, null, false));
    }

    public async Task<Outcome<CheckDto>> UpdateAsync(User caller, int id, CheckUpsert req, CancellationToken ct = default)
    {
        var check = await db.Checks.Include(x => x.Agent).ThenInclude(a => a!.Site).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (check is null) return Outcome.NotFound<CheckDto>();
        if (check.Agent is not null && !ScopeGuard.CanSeeAgent(caller, check.Agent)) return Outcome.Forbidden<CheckDto>();

        if (req.CheckType is not null && req.CheckType != check.CheckType) return Outcome.Invalid<CheckDto>("check_type cannot be changed");
        if (req.Agent is not null && req.Agent != check.Agent?.AgentId) return Outcome.Invalid<CheckDto>("A check cannot be moved to another agent");
        if (req.Policy is not null && req.Policy != check.PolicyId) return Outcome.Invalid<CheckDto>("A check cannot be moved to another policy");

        Apply(check, req);
        var err = CheckValidator.Validate(check.CheckType, Snapshot(check)) ?? await ValidateScriptAsync(check, ct);
        if (err is not null) return Outcome.Invalid<CheckDto>(err);
        if ((check.Name?.Length ?? 0) > 255) return Outcome.Invalid<CheckDto>("name is too long (max 255)");

        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "check", $"{caller.Username} modified check '{check.Name}'",
            check.Agent?.Hostname, check.AgentId, ct: ct);
        return Outcome.Ok(CheckDto.From(check, check.Agent?.AgentId, null, false));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var check = await db.Checks.Include(x => x.Agent).ThenInclude(a => a!.Site).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (check is null) return Outcome.NotFound<bool>();
        if (check.Agent is not null && !ScopeGuard.CanSeeAgent(caller, check.Agent)) return Outcome.Forbidden<bool>();

        db.Checks.Remove(check);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "check", $"{caller.Username} deleted check '{check.Name}'",
            check.Agent?.Hostname, check.AgentId, ct: ct);
        return Outcome.Ok(true);
    }

    private async Task<string?> ValidateScriptAsync(Check c, CancellationToken ct) =>
        c.ScriptId is not null && !await db.Scripts.AnyAsync(s => s.Id == c.ScriptId, ct) ? "script not found" : null;

    private static void Apply(Check c, CheckUpsert r)
    {
        if (r.Name is not null) c.Name = r.Name.Trim();
        if (r.EmailAlert is { } ea) c.EmailAlert = ea;
        if (r.TextAlert is { } ta) c.TextAlert = ta;
        if (r.DashboardAlert is { } da) c.DashboardAlert = da;
        if (r.FailsB4Alert is { } f) c.FailsB4Alert = f;
        if (r.RunInterval is { } ri) c.RunInterval = ri;
        if (r.AlertSeverity is not null) c.AlertSeverity = r.AlertSeverity;
        if (r.ErrorThreshold is { } et) c.ErrorThreshold = et;
        if (r.WarningThreshold is { } wt) c.WarningThreshold = wt;
        if (r.Disk is not null) c.Disk = r.Disk.Trim();
        if (r.Ip is not null) c.Ip = r.Ip.Trim();
        if (r.Script is { } sc) c.ScriptId = sc;
        if (r.ScriptArgs is not null) c.ScriptArgs = r.ScriptArgs.ToList();
        if (r.EnvVars is not null) c.EnvVars = r.EnvVars.ToList();
        if (r.InfoReturnCodes is not null) c.InfoReturnCodes = r.InfoReturnCodes.ToList();
        if (r.WarningReturnCodes is not null) c.WarningReturnCodes = r.WarningReturnCodes.ToList();
        if (r.SuccessReturnCodes is not null) c.SuccessReturnCodes = r.SuccessReturnCodes.ToList();
        if (r.Timeout is { } to) c.Timeout = to;
        if (r.SvcName is not null) c.SvcName = r.SvcName.Trim();
        if (r.SvcDisplayName is not null) c.SvcDisplayName = r.SvcDisplayName;
        if (r.PassIfStartPending is { } ps) c.PassIfStartPending = ps;
        if (r.PassIfSvcNotExist is { } pn) c.PassIfSvcNotExist = pn;
        if (r.RestartIfStopped is { } rs) c.RestartIfStopped = rs;
        if (r.LogName is not null) c.LogName = r.LogName.Trim();
        if (r.EventId is { } ei) c.EventId = ei;
        if (r.EventType is not null) c.EventType = r.EventType;
        if (r.FailWhen is not null) c.FailWhen = r.FailWhen;
        if (r.SearchLastDays is { } sd) c.SearchLastDays = sd;
    }

    private static CheckUpsert Snapshot(Check c) => new()
    {
        Name = c.Name, FailsB4Alert = c.FailsB4Alert, RunInterval = c.RunInterval, AlertSeverity = c.AlertSeverity,
        ErrorThreshold = c.ErrorThreshold, WarningThreshold = c.WarningThreshold, Disk = c.Disk, Ip = c.Ip, Script = c.ScriptId,
        Timeout = c.Timeout, SvcName = c.SvcName, SvcDisplayName = c.SvcDisplayName, LogName = c.LogName, EventId = c.EventId,
        SearchLastDays = c.SearchLastDays
    };
}
