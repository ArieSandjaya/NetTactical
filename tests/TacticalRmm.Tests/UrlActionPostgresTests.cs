using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class UrlActionPostgresTests
{
    private sealed class Audit : IAuditService
    {
        public readonly List<string> Messages = [];
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) { Messages.Add(message); return Task.CompletedTask; }
    }
    private static readonly User Admin = new() { Id = 1, Username = "admin", IsSuperuser = true };

    [PostgresFact]
    public async Task Saves_renames_conflicts_runs_and_deletes()
    {
        await using var db = await PgDb.CreateAsync();
        var c = new Client { Name = "Acme" }; var s = new Site { Name = "Main", Client = c };
        db.AddRange(c, s, new Agent { AgentId = "url-agent-number-00001", Hostname = "PC-1", PublicIp = "1.2.3.4", Site = s, Plat = "windows", Goarch = "amd64" });
        await db.SaveChangesAsync();
        var audit = new Audit(); var svc = new UrlActionService(db, audit);

        var a = await svc.SaveAsync(Admin, null, new UrlActionRequest { Name = "Tickets", Pattern = "https://t.example.com/?h={{agent.hostname}}&c={{client.name}}&ip={{agent.public_ip}}" });
        Assert.True(a.IsOk);
        Assert.Equal(OutcomeStatus.Conflict, (await svc.SaveAsync(Admin, null, new UrlActionRequest { Name = "tickets", Pattern = "https://x.com" })).Status);
        Assert.True((await svc.SaveAsync(Admin, a.Value!.Id, new UrlActionRequest { Name = "Tickets", Pattern = "https://t.example.com/?h={{agent.hostname}}" })).IsOk); // same name on itself is fine
        Assert.Equal(OutcomeStatus.Invalid, (await svc.SaveAsync(Admin, null, new UrlActionRequest { Name = "Bad", Pattern = "javascript:1" })).Status);

        var run = await svc.RunAsync(Admin, a.Value.Id, "url-agent-number-00001");
        Assert.Equal("https://t.example.com/?h=PC-1", run.Value!.Url);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.RunAsync(Admin, a.Value.Id, "nope")).Status);

        Assert.True((await svc.DeleteAsync(Admin, a.Value.Id)).IsOk);
        Assert.Empty((await svc.ListAsync()).Value!);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.DeleteAsync(Admin, a.Value.Id)).Status);
    }
}
