using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class AgentUpdatePostgresTests
{
    private sealed class Audit : IAuditService
    {
        public List<string> Messages = [];
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) { Messages.Add(message); return Task.CompletedTask; }
    }
    private sealed class Bus(object? pingReply) : IAgentCommandBus
    {
        public List<IDictionary<string, object?>> Published = [];
        public Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default) => Task.FromResult(pingReply);
        public Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default) { Published.Add(data); return Task.CompletedTask; }
    }

    private static async Task<(AgentUpdateService svc, Bus bus, Audit audit)> SetupAsync(object? ping, string version, string plat = "windows")
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "One" }; var s = new Site { Name = "S", Client = c };
        db.AddRange(c, s, new Agent { AgentId = "upd-agent-number-0001", Hostname = "PC1", Site = s, Version = version, Plat = plat, Goarch = "amd64" });
        await db.SaveChangesAsync();
        var bus = new Bus(ping); var audit = new Audit();
        return (new AgentUpdateService(db, bus, audit, Options.Create(new TacticalOptions { LatestAgentVer = "2.12.0" })), bus, audit);
    }

    private const string Id = "upd-agent-number-0001";

    [PostgresFact]
    public async Task AnOnlineOldAgentIsToldToUpdateAndItIsAudited()
    {
        var (svc, bus, audit) = await SetupAsync("pong", "2.8.0");
        var r = await svc.UpdateAsync("admin", Id);
        Assert.Equal("started", r.Value!.Status);
        Assert.Equal("agentupdate", Assert.Single(bus.Published)["func"]);
        Assert.Contains("2.8.0", Assert.Single(audit.Messages));
    }

    [PostgresFact]
    public async Task AnOfflineAgentIsNotSentAnything()
    {
        var (svc, bus, _) = await SetupAsync("timeout", "2.8.0");
        var r = await svc.UpdateAsync("admin", Id);
        Assert.Equal(OutcomeStatus.Conflict, r.Status);
        Assert.Empty(bus.Published);
    }

    [PostgresFact]
    public async Task AnUpToDateAgentIsSkippedAndUnknownIsNotFound()
    {
        var (svc, bus, _) = await SetupAsync("pong", "2.12.0");
        Assert.Equal("skipped", (await svc.UpdateAsync("admin", Id)).Value!.Status);
        Assert.Empty(bus.Published);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.UpdateAsync("admin", "nope-nope-nope-nope-1")).Status);
    }
}
