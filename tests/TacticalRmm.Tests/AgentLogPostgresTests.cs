using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class AgentLogPostgresTests
{
    private sealed class NoAudit : IAuditService
    {
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private const string A1 = "log-agent-number-00001", A2 = "log-agent-number-00002";

    private static async Task<(AgentLogService svc, TacticalRmm.Infrastructure.Persistence.AppDbContext db, AgentUserHolder u)> SetupAsync()
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "C" }; var s = new Site { Name = "S", Client = c };
        var user = new User { Username = "admin", Email = "a@b.c" };
        db.AddRange(c, s, user, new Agent { AgentId = A1, Hostname = "PC1", Site = s }, new Agent { AgentId = A2, Hostname = "PC2", Site = s });
        await db.SaveChangesAsync();
        return (new AgentLogService(db, new NoAudit(), NullLogger<AgentLogService>.Instance), db, new AgentUserHolder(user));
    }

    private sealed record AgentUserHolder(User User);

    [PostgresFact]
    public async Task NotesAreAddedListedNewestFirstAndDeleted()
    {
        var (svc, db, h) = await SetupAsync(); await using var _ = db;
        var first = (await svc.AddNoteAsync(h.User, A1, "  first ")).Value!;
        await Task.Delay(20);
        var second = (await svc.AddNoteAsync(h.User, A1, "second")).Value!;
        await svc.AddNoteAsync(h.User, A2, "other asset");
        var list = (await svc.NotesAsync(A1)).Value!;
        Assert.Equal(new[] { "second", "first" }, list.Select(n => n.Note).ToArray());
        Assert.All(list, n => Assert.Equal("admin", n.Username));

        Assert.Equal(OutcomeStatus.NotFound, (await svc.DeleteNoteAsync(h.User, A2, first.Id)).Status);   // a note of another asset
        Assert.True((await svc.DeleteNoteAsync(h.User, A1, first.Id)).IsOk);
        Assert.Equal(new[] { "second" }, (await svc.NotesAsync(A1)).Value!.Select(n => n.Note).ToArray());
        Assert.Equal(OutcomeStatus.Invalid, (await svc.AddNoteAsync(h.User, A1, "   ")).Status);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.AddNoteAsync(h.User, "no-such-agent-00000000", "x")).Status);
    }

    [PostgresFact]
    public async Task HistoryShowsCommandsAndScriptResultsNewestFirst()
    {
        var (svc, db, _) = await SetupAsync(); await using var _db = db;
        var agent = await db.Agents.FirstAsync(a => a.AgentId == A1);
        db.AgentHistory.Add(new AgentHistory { AgentId = agent.Id, Time = DateTime.UtcNow.AddMinutes(-5), Type = "script_run", Command = "Clean temp", Username = "bob",
            ScriptResults = "{\"stdout\":\"cleaned\",\"stderr\":\"\",\"retcode\":0,\"execution_time\":2.5}" });
        await db.SaveChangesAsync();
        await svc.RecordCommandAsync(A1, "admin", "ipconfig /all");
        await svc.RecordCommandAsync(A2, "admin", "other asset");

        var list = (await svc.HistoryAsync(A1, null)).Value!;
        Assert.Equal(new[] { "cmd_run", "script_run" }, list.Select(x => x.Type).ToArray());
        Assert.Equal("ipconfig /all", list[0].Command);
        Assert.Equal(("cleaned", 0, 2.5), (list[1].Stdout, list[1].Retcode, list[1].ExecutionTime));
        Assert.Equal(OutcomeStatus.NotFound, (await svc.HistoryAsync("no-such-agent-00000000", null)).Status);
    }
}
