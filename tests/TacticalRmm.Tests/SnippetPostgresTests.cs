using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class SnippetPostgresTests
{
    private sealed class NoAudit : IAuditService
    {
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) => Task.CompletedTask;
    }
    private static readonly User Admin = new() { Username = "admin", IsSuperuser = true };

    [PostgresFact]
    public async Task ScriptBodyGetsTheSnippetCodeAtRunTime()
    {
        await using var db = await PgDb.CreateAsync();
        var svc = new SnippetService(db, new NoAudit());
        Assert.True((await svc.CreateAsync(Admin, new SnippetUpsert { Name = "log", Code = "function Log($m){ Write-Host $m }" })).IsOk);
        var body = await SnippetExpander.ExpandAsync(db, "{{log}}\nLog 'hi'\n{{missing}}");
        Assert.Equal("function Log($m){ Write-Host $m }\nLog 'hi'\n{{missing}}", body);

        // editing the snippet changes every script that uses it, with no change to the script
        var id = (await db.ScriptSnippets.SingleAsync()).Id;
        await svc.UpdateAsync(Admin, id, new SnippetUpsert { Code = "# v2" });
        Assert.Equal("# v2", await SnippetExpander.ExpandAsync(db, "{{ log }}"));
    }

    [PostgresFact]
    public async Task NameRulesDuplicatesAndRenameAreEnforced()
    {
        await using var db = await PgDb.CreateAsync();
        var svc = new SnippetService(db, new NoAudit());
        Assert.Equal(OutcomeStatus.Invalid, (await svc.CreateAsync(Admin, new SnippetUpsert { Name = "bad name", Code = "x" })).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.CreateAsync(Admin, new SnippetUpsert { Name = "ok", Code = " " })).Status);
        var s = (await svc.CreateAsync(Admin, new SnippetUpsert { Name = "ok", Code = "x" })).Value!;
        Assert.Equal(OutcomeStatus.Conflict, (await svc.CreateAsync(Admin, new SnippetUpsert { Name = "ok", Code = "y" })).Status);
        Assert.Equal(OutcomeStatus.Conflict, (await svc.UpdateAsync(Admin, s.Id, new SnippetUpsert { Name = "other" })).Status);
        Assert.True((await svc.UpdateAsync(Admin, s.Id, new SnippetUpsert { Name = "ok", Description = "d" })).IsOk);
    }

    [PostgresFact]
    public async Task SnippetInUseCannotBeDeletedAndListCountsUsers()
    {
        await using var db = await PgDb.CreateAsync();
        var svc = new SnippetService(db, new NoAudit());
        var s = (await svc.CreateAsync(Admin, new SnippetUpsert { Name = "helper", Code = "x" })).Value!;
        db.Scripts.Add(new Script { Name = "uses", ScriptBody = "{{helper}} run" });
        db.Scripts.Add(new Script { Name = "also", ScriptBody = "{{ helper }}" });
        await db.SaveChangesAsync();
        Assert.Equal(2, (await svc.ListAsync()).Value!.Single().UsedBy);
        Assert.Equal(OutcomeStatus.Conflict, (await svc.DeleteAsync(Admin, s.Id)).Status);
        db.Scripts.RemoveRange(db.Scripts); await db.SaveChangesAsync();
        Assert.True((await svc.DeleteAsync(Admin, s.Id)).IsOk);
    }

    [PostgresFact]
    public async Task SchemaUpgradeCreatesTheSnippetTableOnAnOldDatabase()
    {
        await using var db = await PgDb.CreateAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE script_snippets");
        await SchemaUpgrade.RunAsync(db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        Assert.True((await new SnippetService(db, new NoAudit()).CreateAsync(Admin, new SnippetUpsert { Name = "a", Code = "b" })).IsOk);
    }
}
