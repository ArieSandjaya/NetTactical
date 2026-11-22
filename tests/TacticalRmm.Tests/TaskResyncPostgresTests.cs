using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class TaskResyncPostgresTests
{
    private sealed class Bus : IAgentCommandBus
    {
        public readonly List<string> Agents = [];
        public Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default) { Agents.Add(agentId + ":" + data["func"]); return Task.FromResult<object?>("ok"); }
        public Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default) => Task.CompletedTask;
    }

    [PostgresFact]
    public async Task Tasks_of_a_migrated_agent_are_pushed_again_once_and_old_agents_are_left_alone()
    {
        await using var db = await PgDb.CreateAsync();
        var c = new Client { Name = "One" }; var s = new Site { Name = "S", Client = c };
        var oldAgent = new Agent { AgentId = "old-agent-number-000001", Hostname = "OLD", Site = s, Version = "2.12.0", Plat = "windows", Goarch = "amd64", LastSeen = DateTime.UtcNow };
        var newAgent = new Agent { AgentId = "new-agent-number-000001", Hostname = "NEW", Site = s, Version = "3.0.0", Plat = "windows", Goarch = "amd64", LastSeen = DateTime.UtcNow };
        var t1 = new AutomatedTask { Name = "t1", Agent = oldAgent };
        var t2 = new AutomatedTask { Name = "t2", Agent = newAgent };
        db.AddRange(c, s, oldAgent, newAgent, t1, t2,
            new TaskResult { Agent = oldAgent, Task = t1, SyncStatus = TaskSyncStatus.Synced }, new TaskResult { Agent = newAgent, Task = t2, SyncStatus = TaskSyncStatus.Synced });
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();   // the service runs in its own scope in production; do not let this context's stale copies hide what the SQL did
        var bus = new Bus();
        var svc = new TaskSyncService(db, bus, NullLogger<TaskSyncService>.Instance);
        await svc.SyncPendingAsync();

        Assert.Equal(["new-agent-number-000001:schedtask"], bus.Agents);
        db.ChangeTracker.Clear();
        Assert.True((await db.Agents.SingleAsync(a => a.Hostname == "NEW")).TasksResynced);
        Assert.False((await db.Agents.SingleAsync(a => a.Hostname == "OLD")).TasksResynced);

        await svc.SyncPendingAsync();     // only once
        Assert.Single(bus.Agents);
    }
}
