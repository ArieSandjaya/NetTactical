using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class TaskAdminService(AppDbContext db, ITaskService tasks, ITaskSyncService sync, IAuditService audit) : ITaskAdminService
{
    public async Task<Outcome<IReadOnlyList<TaskDto>>> ListForAgentAsync(User caller, string agentId, CancellationToken ct = default)
    {
        var agent = await db.Agents.AsNoTracking().Include(a => a.Site).FirstOrDefaultAsync(a => a.AgentId == agentId, ct);
        if (agent is null) return Outcome.NotFound<IReadOnlyList<TaskDto>>("Agent not found");
        if (!ScopeGuard.CanSeeAgent(caller, agent)) return Outcome.Forbidden<IReadOnlyList<TaskDto>>();

        var policyActive = agent.PolicyId is not null && !agent.BlockPolicyInheritance
            && await db.Policies.AnyAsync(p => p.Id == agent.PolicyId && p.Active, ct);
        var list = await db.AutomatedTasks.AsNoTracking()
            .Where(t => !t.PendingDeletion && (t.AgentId == agent.Id || (policyActive && t.PolicyId == agent.PolicyId)))
            .OrderBy(t => t.Name).ToListAsync(ct);
        var ids = list.Select(t => t.Id).ToList();
        var results = await db.TaskResults.AsNoTracking().Where(r => r.AgentId == agent.Id && ids.Contains(r.TaskId))
            .ToDictionaryAsync(r => r.TaskId, ct);
        IReadOnlyList<TaskDto> dtos = list.Select(t => TaskDto.From(t, t.AgentId is null ? null : agent.AgentId, results.GetValueOrDefault(t.Id))).ToList();
        return Outcome.Ok(dtos);
    }

    public async Task<Outcome<IReadOnlyList<TaskDto>>> ListForPolicyAsync(User caller, int policyId, CancellationToken ct = default)
    {
        if (!await db.Policies.AnyAsync(p => p.Id == policyId, ct)) return Outcome.NotFound<IReadOnlyList<TaskDto>>("Policy not found");
        var list = await db.AutomatedTasks.AsNoTracking().Where(t => t.PolicyId == policyId && !t.PendingDeletion).OrderBy(t => t.Name).ToListAsync(ct);
        IReadOnlyList<TaskDto> dtos = list.Select(t => TaskDto.From(t, null, null)).ToList();
        return Outcome.Ok(dtos);
    }

    public async Task<Outcome<TaskDto>> GetAsync(User caller, int id, CancellationToken ct = default)
    {
        var t = await db.AutomatedTasks.AsNoTracking().Include(x => x.Agent).ThenInclude(a => a!.Site).FirstOrDefaultAsync(x => x.Id == id && !x.PendingDeletion, ct);
        if (t is null) return Outcome.NotFound<TaskDto>();
        if (t.Agent is not null && !ScopeGuard.CanSeeAgent(caller, t.Agent)) return Outcome.Forbidden<TaskDto>();
        var r = t.AgentId is null ? null : await db.TaskResults.AsNoTracking().FirstOrDefaultAsync(x => x.TaskId == id && x.AgentId == t.AgentId, ct);
        return Outcome.Ok(TaskDto.From(t, t.Agent?.AgentId, r));
    }

    public async Task<Outcome<TaskDto>> CreateAsync(User caller, TaskUpsert req, CancellationToken ct = default)
    {
        if ((req.Agent is null) == (req.Policy is null)) return Outcome.Invalid<TaskDto>("Specify exactly one of 'agent' or 'policy'");
        if (req.Actions.ValueKind == JsonValueKind.Undefined) return Outcome.Invalid<TaskDto>("actions is required");

        Agent? agent = null;
        if (req.Agent is not null)
        {
            agent = await db.Agents.Include(a => a.Site).FirstOrDefaultAsync(a => a.AgentId == req.Agent, ct);
            if (agent is null) return Outcome.NotFound<TaskDto>("Agent not found");
            if (!ScopeGuard.CanSeeAgent(caller, agent)) return Outcome.Forbidden<TaskDto>();
        }
        else if (!await db.Policies.AnyAsync(p => p.Id == req.Policy, ct)) return Outcome.NotFound<TaskDto>("Policy not found");

        var task = new AutomatedTask { AgentId = agent?.Id, PolicyId = req.Policy, ManagedByPolicy = false };
        Apply(task, req);
        var err = await ValidateAsync(task, req.Actions, ct);
        if (err is not null) return Outcome.Invalid<TaskDto>(err);

        db.AutomatedTasks.Add(task);
        await db.SaveChangesAsync(ct);
        await MarkForSyncAsync(task, ct);

        await audit.LogAsync(caller.Username, AuditAction.Add, "automatedtask",
            $"{caller.Username} added task '{task.Name}'" + (agent is null ? $" to policy {task.PolicyId}" : $" on {agent.Hostname}"),
            agent?.Hostname, agent?.Id, ct: ct);
        return Outcome.Ok(TaskDto.From(task, agent?.AgentId, null));
    }

    public async Task<Outcome<TaskDto>> UpdateAsync(User caller, int id, TaskUpsert req, CancellationToken ct = default)
    {
        var task = await db.AutomatedTasks.Include(x => x.Agent).ThenInclude(a => a!.Site).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (task is null || task.PendingDeletion) return Outcome.NotFound<TaskDto>();
        if (task.Agent is not null && !ScopeGuard.CanSeeAgent(caller, task.Agent)) return Outcome.Forbidden<TaskDto>();
        if (req.Agent is not null && req.Agent != task.Agent?.AgentId) return Outcome.Invalid<TaskDto>("A task cannot be moved to another agent");
        if (req.Policy is not null && req.Policy != task.PolicyId) return Outcome.Invalid<TaskDto>("A task cannot be moved to another policy");

        Apply(task, req);
        var err = await ValidateAsync(task, req.Actions, ct);
        if (err is not null) return Outcome.Invalid<TaskDto>(err);

        await db.SaveChangesAsync(ct);
        await MarkForSyncAsync(task, ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "automatedtask", $"{caller.Username} modified task '{task.Name}'",
            task.Agent?.Hostname, task.AgentId, ct: ct);
        return Outcome.Ok(TaskDto.From(task, task.Agent?.AgentId, null));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var task = await db.AutomatedTasks.Include(x => x.Agent).ThenInclude(a => a!.Site).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (task is null) return Outcome.NotFound<bool>();
        if (task.Agent is not null && !ScopeGuard.CanSeeAgent(caller, task.Agent)) return Outcome.Forbidden<bool>();

        // Task harus dihapus dari Task Scheduler agent dulu; baris DB dihapus TaskSyncJob setelah agent konfirmasi.
        task.PendingDeletion = true;
        task.Enabled = false;
        var results = await db.TaskResults.Where(r => r.TaskId == id).ToListAsync(ct);
        foreach (var r in results) r.SyncStatus = TaskSyncStatus.PendingDeletion;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(caller.Username, AuditAction.Delete, "automatedtask", $"{caller.Username} deleted task '{task.Name}'",
            task.Agent?.Hostname, task.AgentId, ct: ct);
        if (task.AgentId is { } aid) await sync.SyncOneAsync(task.Id, aid, ct);   // coba langsung; sisanya oleh job
        return Outcome.Ok(true);
    }

    public async Task<Outcome<bool>> RunNowAsync(User caller, int id, string? agentId, CancellationToken ct = default)
    {
        var task = await db.AutomatedTasks.AsNoTracking().Include(x => x.Agent).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (task is null || task.PendingDeletion) return Outcome.NotFound<bool>();
        var targetId = task.Agent?.AgentId ?? agentId;
        if (targetId is null) return Outcome.Invalid<bool>("agent_id is required to run a policy task");

        var agent = await db.Agents.Include(a => a.Site).FirstOrDefaultAsync(a => a.AgentId == targetId, ct);
        if (agent is null) return Outcome.NotFound<bool>("Agent not found");
        if (!ScopeGuard.CanSeeAgent(caller, agent)) return Outcome.Forbidden<bool>();
        if (task.AgentId is not null && task.AgentId != agent.Id) return Outcome.Invalid<bool>("Task does not belong to this agent");
        if (task.PolicyId is not null && (agent.PolicyId != task.PolicyId || agent.BlockPolicyInheritance))
            return Outcome.Invalid<bool>("Task is not applied to this agent");
        if (agent.Plat != AgentPlat.Windows) return Outcome.Invalid<bool>("Scheduled tasks run on Windows agents only");

        await tasks.RunNowAsync(agent, id, ct);
        await audit.LogAsync(caller.Username, AuditAction.ExecScript, "automatedtask", $"{caller.Username} ran task '{task.Name}' on {agent.Hostname}",
            agent.Hostname, agent.Id, ct: ct);
        return Outcome.Ok(true);
    }

    // ------------------------------------------------------------------------------------------------ helpers
    private async Task MarkForSyncAsync(AutomatedTask task, CancellationToken ct)
    {
        var agentIds = task.AgentId is { } a
            ? [a]
            : await db.Agents.Where(x => x.PolicyId == task.PolicyId && !x.BlockPolicyInheritance).Select(x => x.Id).ToListAsync(ct);
        var existing = await db.TaskResults.Where(r => r.TaskId == task.Id).ToListAsync(ct);
        foreach (var aid in agentIds)
        {
            var r = existing.FirstOrDefault(x => x.AgentId == aid);
            if (r is null) db.TaskResults.Add(new TaskResult { AgentId = aid, TaskId = task.Id, SyncStatus = TaskSyncStatus.NotSynced });
            else r.SyncStatus = TaskSyncStatus.NotSynced;
        }
        await db.SaveChangesAsync(ct);
        if (task.AgentId is { } one) await sync.SyncOneAsync(task.Id, one, ct);
    }

    private async Task<string?> ValidateAsync(AutomatedTask task, JsonElement actions, CancellationToken ct)
    {
        var err = TaskValidator.Validate(Snapshot(task), task.TaskType, DateTime.UtcNow);
        if (err is not null) return err;

        if (actions.ValueKind != JsonValueKind.Undefined)
        {
            err = TaskValidator.ValidateActions(actions);
            if (err is not null) return err;
            var scriptIds = actions.EnumerateArray()
                .Where(a => a.TryGetProperty("type", out var t) && t.GetString() == "script")
                .Select(a => a.GetProperty("script").GetInt32()).Distinct().ToList();
            if (scriptIds.Count > 0)
            {
                var found = await db.Scripts.CountAsync(s => scriptIds.Contains(s.Id), ct);
                if (found != scriptIds.Count) return "One or more scripts in actions do not exist";
            }
            task.Actions = actions.GetRawText();
        }

        if (task.TaskType == TaskType.CheckFailure)
        {
            var check = await db.Checks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == task.AssignedCheckId, ct);
            if (check is null) return "assigned_check not found";
            if (check.AgentId != task.AgentId || check.PolicyId != task.PolicyId) return "assigned_check must belong to the same agent/policy as the task";
        }
        else task.AssignedCheckId = null;
        return null;
    }

    private static void Apply(AutomatedTask t, TaskUpsert r)
    {
        if (r.Name is not null) t.Name = r.Name.Trim();
        if (r.Enabled is { } en) t.Enabled = en;
        if (r.ContinueOnError is { } ce) t.ContinueOnError = ce;
        if (r.AlertSeverity is not null) t.AlertSeverity = r.AlertSeverity;
        if (r.EmailAlert is { } ea) t.EmailAlert = ea;
        if (r.TextAlert is { } ta) t.TextAlert = ta;
        if (r.DashboardAlert is { } da) t.DashboardAlert = da;
        if (r.TaskType is not null) t.TaskType = r.TaskType;
        if (r.RunTimeDate is { } rt) t.RunTimeDate = DateTime.SpecifyKind(rt, rt.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : rt.Kind).ToUniversalTime();
        if (r.ExpireDate is { } ex) t.ExpireDate = DateTime.SpecifyKind(ex, ex.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : ex.Kind).ToUniversalTime();
        if (r.DailyInterval is { } di) t.DailyInterval = di;
        if (r.WeeklyInterval is { } wi) t.WeeklyInterval = wi;
        if (r.RunTimeBitWeekdays is { } bw) t.RunTimeBitWeekdays = bw;
        if (r.MonthlyDaysOfMonth is { } md) t.MonthlyDaysOfMonth = md;
        if (r.MonthlyMonthsOfYear is { } mm) t.MonthlyMonthsOfYear = mm;
        if (r.MonthlyWeeksOfMonth is { } mw) t.MonthlyWeeksOfMonth = mw;
        if (r.TaskRepetitionDuration is not null) t.TaskRepetitionDuration = r.TaskRepetitionDuration;
        if (r.TaskRepetitionInterval is not null) t.TaskRepetitionInterval = r.TaskRepetitionInterval;
        if (r.StopTaskAtDurationEnd is { } sd) t.StopTaskAtDurationEnd = sd;
        if (r.RandomTaskDelay is not null) t.RandomTaskDelay = r.RandomTaskDelay;
        if (r.RemoveIfNotScheduled is { } rn) t.RemoveIfNotScheduled = rn;
        if (r.RunAsapAfterMissed is { } ra) t.RunAsapAfterMissed = ra;
        if (r.TaskInstancePolicy is { } ip) t.TaskInstancePolicy = ip;
        if (r.AssignedCheck is { } ac) t.AssignedCheckId = ac;
    }

    private static TaskUpsert Snapshot(AutomatedTask t) => new()
    {
        Name = t.Name, AlertSeverity = t.AlertSeverity, RunTimeDate = t.RunTimeDate, ExpireDate = t.ExpireDate,
        DailyInterval = t.DailyInterval, WeeklyInterval = t.WeeklyInterval, RunTimeBitWeekdays = t.RunTimeBitWeekdays,
        MonthlyDaysOfMonth = t.MonthlyDaysOfMonth, MonthlyMonthsOfYear = t.MonthlyMonthsOfYear, MonthlyWeeksOfMonth = t.MonthlyWeeksOfMonth,
        TaskRepetitionDuration = t.TaskRepetitionDuration, TaskRepetitionInterval = t.TaskRepetitionInterval, RandomTaskDelay = t.RandomTaskDelay,
        TaskInstancePolicy = t.TaskInstancePolicy, AssignedCheck = t.AssignedCheckId
    };
}
