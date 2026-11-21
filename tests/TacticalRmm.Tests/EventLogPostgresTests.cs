using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class EventLogPostgresTests
{
    private sealed class Bus(object? reply) : IAgentCommandBus
    {
        public readonly List<IDictionary<string, object?>> Sent = [];
        public Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default) { Sent.Add(data); return Task.FromResult(reply); }
        public Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task<(EventLogService svc, Bus bus, TacticalRmm.Infrastructure.Persistence.AppDbContext db)> SetupAsync(object? reply)
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "C" }; var s = new Site { Name = "S", Client = c };
        db.AddRange(c, s, new Agent { AgentId = "evt-agent-number-0001", Hostname = "W", Plat = "windows", Site = s }, new Agent { AgentId = "evt-agent-number-0002", Hostname = "L", Plat = "linux", Site = s });
        await db.SaveChangesAsync();
        var bus = new Bus(reply);
        return (new EventLogService(db, bus), bus, db);
    }

    [PostgresFact]
    public async Task EventsAreReadFromTheScriptOutput()
    {
        var (svc, bus, db) = await SetupAsync(new Dictionary<object, object?> { ["stdout"] = """@@OK@@[{"time":"2024-05-01T10:00:00Z","id":41,"level":"Critical","provider":"Kernel-Power","message":"rebooted","record":9}]""", ["retcode"] = 0 });
        await using var _ = db;
        var r = await svc.ReadAsync("evt-agent-number-0001", "System", "critical", 24, 100, null, null);
        Assert.Equal(41, r.Value!.Entries.Single().Id);
        Assert.Equal("runscript", bus.Sent.Single()["func"]);
    }

    [PostgresFact]
    public async Task BadInputLinuxUnknownAndOfflineAreHandled()
    {
        var (svc, bus, db) = await SetupAsync("timeout");
        await using var _ = db;
        Assert.Equal(OutcomeStatus.Invalid, (await svc.ReadAsync("evt-agent-number-0001", "Other", "all", 24, 100, null, null)).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.ReadAsync("evt-agent-number-0002", "System", "all", 24, 100, null, null)).Status);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.ReadAsync("nope-nope-nope-nope-1", "System", "all", 24, 100, null, null)).Status);
        Assert.Empty(bus.Sent);
        Assert.Equal(OutcomeStatus.Conflict, (await svc.ReadAsync("evt-agent-number-0001", "System", "all", 24, 100, null, null)).Status);
    }
}
