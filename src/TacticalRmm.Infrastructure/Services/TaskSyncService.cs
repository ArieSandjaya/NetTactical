using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>Pushes task definitions to the agent's Task Scheduler (schedtask) or deletes them (delschedtask).</summary>
public sealed class TaskSyncService(AppDbContext db, IAgentCommandBus bus, ILogger<TaskSyncService> log) : ITaskSyncService
{
    private static readonly string[] Pending = [TaskSyncStatus.Initial, TaskSyncStatus.NotSynced, TaskSyncStatus.PendingDeletion];
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<int> SyncPendingAsync(CancellationToken ct = default)
    {
        await EnsurePolicyResultsAsync(ct);
        await PurgeOrphanDeletionsAsync(ct);

        var recent = DateTime.UtcNow.AddMinutes(-10);
        var work = await db.TaskResults.AsNoTracking()
            .Where(r => Pending.Contains(r.SyncStatus) && r.Agent.LastSeen > recent)
            .OrderBy(r => r.Id).Take(200).Select(r => new { r.TaskId, r.AgentId }).ToListAsync(ct);

        var done = 0;
        using var gate = new SemaphoreSlim(10);
        await Task.WhenAll(work.Select(async w =>
        {
            await gate.WaitAsync(ct);
            try
            {
                // NATS requests run in parallel; DbContext access (not thread-safe) is serialised via _dbLock.
                await SyncOneInternalAsync(w.TaskId, w.AgentId, ct);
                Interlocked.Increment(ref done);
            }
            finally { gate.Release(); }
        }));
        return done;
    }

    public Task SyncOneAsync(int taskId, int agentId, CancellationToken ct = default) => SyncOneInternalAsync(taskId, agentId, ct);

    private readonly SemaphoreSlim _dbLock = new(1, 1);

    private async Task SyncOneInternalAsync(int taskId, int agentId, CancellationToken ct)
    {
        AutomatedTask? task; Agent? agent; TaskResult? result;
        await _dbLock.WaitAsync(ct);
        try
        {
            task = await db.AutomatedTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
            agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
            result = await db.TaskResults.FirstOrDefaultAsync(r => r.TaskId == taskId && r.AgentId == agentId, ct);
        }
        finally { _dbLock.Release(); }
        if (task is null || agent is null || result is null) return;

        // Non-Windows agents have no Task Scheduler: treat as already synced / delete immediately.
        if (agent.Plat != AgentPlat.Windows)
        {
            await Save(() =>
            {
                if (task.PendingDeletion) db.TaskResults.Remove(result); else result.SyncStatus = TaskSyncStatus.Synced;
            }, ct);
            await RemoveTaskIfDrained(taskId, ct);
            return;
        }

        if (task.PendingDeletion)
        {
            var reply = await bus.RequestAsync(agent.AgentId, new Dictionary<string, object?>
            {
                ["func"] = "delschedtask",
                ["schedtaskpayload"] = new Dictionary<string, object?> { ["name"] = task.WinTaskName }
            }, Timeout, ct);
            // "ok" or the task no longer exists on the agent -> delete the record; otherwise retry in the next round.
            if (reply is string s && (s == "ok" || s.Contains("does not exist", StringComparison.OrdinalIgnoreCase) || s.Contains("not found", StringComparison.OrdinalIgnoreCase)))
            {
                await Save(() => db.TaskResults.Remove(result), ct);
                await RemoveTaskIfDrained(taskId, ct);
            }
            else log.LogWarning("delschedtask {Task} on {Agent} -> {Reply}", task.WinTaskName, agent.Hostname, reply);
            return;
        }

        var payload = TaskPayloadBuilder.Build(task, DateTime.UtcNow);
        var r = await bus.RequestAsync(agent.AgentId, new Dictionary<string, object?> { ["func"] = "schedtask", ["schedtaskpayload"] = payload }, Timeout, ct);
        await Save(() => result.SyncStatus = r is string ok && ok == "ok" ? TaskSyncStatus.Synced : TaskSyncStatus.NotSynced, ct);
        if (r is not string { } str || str != "ok") log.LogWarning("schedtask {Task} on {Agent} -> {Reply}", task.WinTaskName, agent.Hostname, r);
    }

    private async Task Save(Action mutate, CancellationToken ct)
    {
        await _dbLock.WaitAsync(ct);
        try { mutate(); await db.SaveChangesAsync(ct); }
        finally { _dbLock.Release(); }
    }

    private async Task RemoveTaskIfDrained(int taskId, CancellationToken ct)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            if (await db.TaskResults.AnyAsync(r => r.TaskId == taskId, ct)) return;
            var t = await db.AutomatedTasks.FirstOrDefaultAsync(x => x.Id == taskId && x.PendingDeletion, ct);
            if (t is null) return;
            db.AutomatedTasks.Remove(t);
            await db.SaveChangesAsync(ct);
        }
        finally { _dbLock.Release(); }
    }

    /// <summary>An active policy task must have a TaskResult row for every agent that inherits it (new agent / policy change).</summary>
    private async Task EnsurePolicyResultsAsync(CancellationToken ct)
    {
        var pairs = await (from t in db.AutomatedTasks
                           join p in db.Policies on t.PolicyId equals (int?)p.Id
                           join a in db.Agents on t.PolicyId equals a.PolicyId
                           where p.Active && !t.PendingDeletion && !a.BlockPolicyInheritance
                                 && !db.TaskResults.Any(r => r.TaskId == t.Id && r.AgentId == a.Id)
                           select new { TaskId = t.Id, AgentId = a.Id }).OrderBy(x => x.TaskId).ThenBy(x => x.AgentId).Take(500).ToListAsync(ct);
        if (pairs.Count == 0) return;
        foreach (var x in pairs) db.TaskResults.Add(new TaskResult { TaskId = x.TaskId, AgentId = x.AgentId, SyncStatus = TaskSyncStatus.NotSynced });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Tasks pending deletion that have no target agent (never synced) are deleted immediately.</summary>
    private async Task PurgeOrphanDeletionsAsync(CancellationToken ct)
    {
        var orphans = await db.AutomatedTasks.Where(t => t.PendingDeletion && !db.TaskResults.Any(r => r.TaskId == t.Id)).ToListAsync(ct);
        if (orphans.Count == 0) return;
        db.AutomatedTasks.RemoveRange(orphans);
        await db.SaveChangesAsync(ct);
    }
}
