using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class RemoteToolsPostgresTests
{
    private const string Win = "tool-agent-number-0001", Lin = "tool-agent-number-0002";
    private static readonly User Caller = new() { Id = 1, Username = "admin" };

    private sealed class Audit : IAuditService
    {
        public readonly List<string> Messages = [];
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) { Messages.Add(message); return Task.CompletedTask; }
    }

    private sealed class Bus(Func<IDictionary<string, object?>, object?> reply) : IAgentCommandBus
    {
        public readonly List<IDictionary<string, object?>> Sent = [];
        public Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default) { Sent.Add(data); return Task.FromResult(reply(data)); }
        public Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task<(RemoteToolsService svc, Bus bus, Audit audit, TacticalRmm.Infrastructure.Persistence.AppDbContext db)> SetupAsync(Func<IDictionary<string, object?>, object?> reply)
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "C" }; var s = new Site { Name = "S", Client = c };
        db.AddRange(c, s, new Agent { AgentId = Win, Hostname = "WIN1", Plat = "windows", Site = s }, new Agent { AgentId = Lin, Hostname = "LIN1", Plat = "linux", Site = s });
        await db.SaveChangesAsync();
        var bus = new Bus(reply); var audit = new Audit();
        return (new RemoteToolsService(db, bus, audit), bus, audit, db);
    }

    private static Dictionary<object, object?> Map(params (string k, object? v)[] kv) => kv.ToDictionary(x => (object)x.k, x => x.v);

    [PostgresFact]
    public async Task ServicesAreListedFromTheAgentsReply()
    {
        var (svc, bus, _, db) = await SetupAsync(_ => new List<object?> { Map(("name", "Spooler"), ("display_name", "Print Spooler"), ("status", "running")) });
        await using var _db = db;
        var r = await svc.ServicesAsync(Win);
        Assert.Equal("Spooler", r.Value!.Single().Name);
        Assert.Equal("winservices", bus.Sent.Single()["func"]);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.ServicesAsync(Lin)).Status);          // services are Windows only
        Assert.Equal(OutcomeStatus.NotFound, (await svc.ServicesAsync("no-such-agent-00000000")).Status);
    }

    [PostgresFact]
    public async Task AnOfflineAgentIsAConflictNotAnEmptyList()
    {
        var (svc, _, _, db) = await SetupAsync(_ => "timeout");
        await using var _db = db;
        Assert.Equal(OutcomeStatus.Conflict, (await svc.ServicesAsync(Win)).Status);
        Assert.Equal(OutcomeStatus.Conflict, (await svc.ProcessesAsync(Win)).Status);
    }

    [PostgresFact]
    public async Task AServiceActionSendsNameAndActionAndIsAudited()
    {
        var (svc, bus, audit, db) = await SetupAsync(_ => Map(("success", true), ("errormsg", "")));
        await using var _db = db;
        var r = await svc.ServiceActionAsync(Caller, Win, new ServiceActionRequest(" Spooler ", "Restart"));
        Assert.True(r.IsOk);
        var sent = bus.Sent.Single();
        Assert.Equal("winsvcaction", sent["func"]);
        var payload = (IDictionary<string, object?>)sent["payload"]!;
        Assert.Equal(("Spooler", "restart"), ((string)payload["name"]!, (string)payload["action"]!));
        Assert.Contains(audit.Messages, m => m.Contains("restart") && m.Contains("Spooler") && m.Contains("WIN1"));
    }

    [PostgresFact]
    public async Task ABadServiceActionNeverReachesTheAgent()
    {
        var (svc, bus, _, db) = await SetupAsync(_ => "ok");
        await using var _db = db;
        Assert.Equal(OutcomeStatus.Invalid, (await svc.ServiceActionAsync(Caller, Win, new ServiceActionRequest("Spooler", "delete"))).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.ServiceActionAsync(Caller, Win, new ServiceActionRequest("", "stop"))).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.ServiceActionAsync(Caller, Lin, new ServiceActionRequest("x", "stop"))).Status);
        Assert.Empty(bus.Sent);
    }

    [PostgresFact]
    public async Task AFailedServiceActionShowsTheAgentsReason()
    {
        var (svc, _, _, db) = await SetupAsync(_ => Map(("success", false), ("errormsg", "Access is denied.")));
        await using var _db = db;
        var r = await svc.ServiceActionAsync(Caller, Win, new ServiceActionRequest("Spooler", "stop"));
        Assert.Equal(OutcomeStatus.Conflict, r.Status);
        Assert.Contains("Access is denied.", r.Error);
    }

    [PostgresFact]
    public async Task ProcessesAreListedAndKilledByPid()
    {
        var (svc, bus, audit, db) = await SetupAsync(d => (string)d["func"]! == "procs"
            ? new List<object?> { Map(("name", "chrome.exe"), ("pid", 4321L), ("membytes", 1000L)) } : "ok");
        await using var _db = db;
        Assert.Equal(4321, (await svc.ProcessesAsync(Lin)).Value!.Single().Pid);            // processes work on every platform
        Assert.True((await svc.KillAsync(Caller, Lin, new KillRequest(4321, "chrome.exe"))).IsOk);
        var kill = bus.Sent.Last();
        Assert.Equal(("killproc", 4321), ((string)kill["func"]!, (int)kill["procpid"]!));
        Assert.Contains(audit.Messages, m => m.Contains("4321") && m.Contains("chrome.exe") && m.Contains("LIN1"));
        Assert.Equal(OutcomeStatus.Invalid, (await svc.KillAsync(Caller, Lin, new KillRequest(4))).Status);
        Assert.Equal(2, bus.Sent.Count);                                                     // pid 4 never went out
    }
}
