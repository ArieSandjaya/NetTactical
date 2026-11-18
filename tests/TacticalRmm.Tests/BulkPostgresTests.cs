using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class BulkPostgresTests
{
    private const string A1 = "bulk-agent-number-0001", A2 = "bulk-agent-number-0002", A3 = "bulk-agent-number-0003";

    private sealed class NoAudit : IAuditService
    {
        public List<string> Messages = [];
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) { lock (Messages) Messages.Add(message); return Task.CompletedTask; }
    }

    private sealed class Perms(bool has, params string[] denyAgents) : IPermissionService
    {
        public bool HasPermission(User user, string permission) => has;
        public Task<bool> HasPermissionOnAgentAsync(User user, string agentId, CancellationToken ct = default) => Task.FromResult(!denyAgents.Contains(agentId));
    }

    private sealed class Bus : IAgentCommandBus
    {
        public readonly List<(string agent, string func)> Calls = [];
        public Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default)
        {
            lock (Calls) Calls.Add((agentId, (string)data["func"]!));
            return Task.FromResult<object?>(agentId == A2 ? "timeout" : data["func"] as string == "rebootnow" ? "ok" : "output of " + agentId);
        }
        public Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task<(BulkService svc, Bus bus, NoAudit audit, AppDbContext db)> SetupAsync(IPermissionService perms)
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "One" }; var s = new Site { Name = "S", Client = c };
        db.AddRange(c, s, new Agent { AgentId = A1, Hostname = "PC1", Site = s }, new Agent { AgentId = A2, Hostname = "PC2", Site = s }, new Agent { AgentId = A3, Hostname = "PC3", Site = s });
        await db.SaveChangesAsync();
        var cs = db.Database.GetConnectionString()!;
        var audit = new NoAudit(); var bus = new Bus();
        var services = new ServiceCollection()
            .AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options))
            .AddSingleton(perms).AddSingleton<IAuditService>(audit).BuildServiceProvider();
        return (new BulkService(services.GetRequiredService<IServiceScopeFactory>(), bus, NullLogger<BulkService>.Instance), bus, audit, db);
    }

    private static async Task<BulkJobDto> WaitAsync(BulkService svc, User u, string id)
    {
        for (var i = 0; i < 100; i++)
        {
            var j = (await svc.GetAsync(u, id)).Value!;
            if (j.State == "done") return j;
            await Task.Delay(50);
        }
        throw new TimeoutException("job did not finish");
    }

    private static readonly User Caller = new() { Id = 7, Username = "admin" };

    [PostgresFact]
    public async Task ACommandRunsOnEveryAssetAndEachGetsItsOwnResult()
    {
        var (svc, bus, audit, db) = await SetupAsync(new Perms(true));
        await using var _ = db;
        var started = await svc.StartAsync(Caller, new BulkRequest("command", [A1, A2, A3], "ipconfig", "cmd", 30));
        Assert.True(started.IsOk);
        var job = await WaitAsync(svc, Caller, started.Value!.Id);
        Assert.Equal(new[] { "PC1", "PC2", "PC3" }, job.Targets.Select(t => t.Hostname).ToArray());
        Assert.Equal(new[] { "ok", "offline", "ok" }, job.Targets.Select(t => t.Status).ToArray());   // PC2 does not answer
        Assert.Equal("output of " + A1, job.Targets[0].Message);
        Assert.Equal(3, bus.Calls.Count(c => c.func == "rawcmd"));
        Assert.Contains(audit.Messages, m => m.Contains("bulk command") && m.Contains("PC1") && m.Contains("ipconfig"));
    }

    [PostgresFact]
    public async Task ARebootOnlyCountsWhenTheAgentSaysOk()
    {
        var (svc, _, _, db) = await SetupAsync(new Perms(true));
        await using var _db = db;
        var job = await WaitAsync(svc, Caller, (await svc.StartAsync(Caller, new BulkRequest("reboot", [A1, A2]))).Value!.Id);
        Assert.Equal(new[] { "ok", "offline" }, job.Targets.Select(t => t.Status).ToArray());
    }

    [PostgresFact]
    public async Task WithoutThePermissionNothingRuns()
    {
        var (svc, bus, _, db) = await SetupAsync(new Perms(false));
        await using var _db = db;
        var r = await svc.StartAsync(Caller, new BulkRequest("command", [A1], "dir", "cmd"));
        Assert.Equal(OutcomeStatus.Forbidden, r.Status);
        Assert.Empty(bus.Calls);
    }

    [PostgresFact]
    public async Task OneAssetOutsideTheCallersScopeRefusesTheWholeRequest()
    {
        var (svc, bus, _, db) = await SetupAsync(new Perms(true, A3));
        await using var _db = db;
        var r = await svc.StartAsync(Caller, new BulkRequest("command", [A1, A3], "dir", "cmd"));
        Assert.Equal(OutcomeStatus.Forbidden, r.Status);
        await Task.Delay(200);
        Assert.Empty(bus.Calls);   // not even the allowed asset ran
    }

    [PostgresFact]
    public async Task UnknownAssetsAndForeignJobsAreNotFound()
    {
        var (svc, _, _, db) = await SetupAsync(new Perms(true));
        await using var _db = db;
        Assert.Equal(OutcomeStatus.NotFound, (await svc.StartAsync(Caller, new BulkRequest("command", [A1, "bulk-agent-number-9999"], "dir", "cmd"))).Status);
        var job = (await svc.StartAsync(Caller, new BulkRequest("reboot", [A1]))).Value!;
        var other = new User { Id = 8, Username = "other" };
        Assert.Equal(OutcomeStatus.NotFound, (await svc.GetAsync(other, job.Id)).Status);
        Assert.True((await svc.GetAsync(new User { Id = 9, Username = "root", IsSuperuser = true }, job.Id)).IsOk);
    }
}
