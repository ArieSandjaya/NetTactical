using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class FileBrowserPostgresTests
{
    private const string Win = "file-agent-number-0001", Lin = "file-agent-number-0002";
    private static readonly User Caller = new() { Id = 1, Username = "admin" };

    private sealed class Audit : IAuditService
    {
        public readonly List<string> Messages = [];
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) { Messages.Add(message); return Task.CompletedTask; }
    }
    private sealed class Log : IAgentLogService
    {
        public readonly List<string> Commands = [];
        public Task<Outcome<IReadOnlyList<HistoryEntryDto>>> HistoryAsync(string agentId, int? take, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Outcome<IReadOnlyList<NoteDto>>> NotesAsync(string agentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Outcome<NoteDto>> AddNoteAsync(User caller, string agentId, string? text, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Outcome<bool>> DeleteNoteAsync(User caller, string agentId, int noteId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordCommandAsync(string agentId, string username, string command, CancellationToken ct = default) { Commands.Add(command); return Task.CompletedTask; }
    }
    private sealed class Bus(Func<IDictionary<string, object?>, object?> reply) : IAgentCommandBus
    {
        public readonly List<IDictionary<string, object?>> Sent = [];
        public Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default) { Sent.Add(data); return Task.FromResult(reply(data)); }
        public Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static Dictionary<object, object?> Out(string stdout) => new() { ["stdout"] = stdout, ["stderr"] = "", ["retcode"] = 0 };

    private static async Task<(FileBrowserService svc, Bus bus, Audit audit, Log log, TacticalRmm.Infrastructure.Persistence.AppDbContext db)> SetupAsync(Func<IDictionary<string, object?>, object?> reply)
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "C" }; var s = new Site { Name = "S", Client = c };
        db.AddRange(c, s, new Agent { AgentId = Win, Hostname = "WIN1", Plat = "windows", Site = s }, new Agent { AgentId = Lin, Hostname = "LIN1", Plat = "linux", Site = s });
        await db.SaveChangesAsync();
        var bus = new Bus(reply); var audit = new Audit(); var log = new Log();
        return (new FileBrowserService(db, bus, audit, log), bus, audit, log, db);
    }

    [PostgresFact]
    public async Task AFolderIsListedFromTheScriptOutput()
    {
        var (svc, bus, _, _, db) = await SetupAsync(_ => Out("""@@OK@@[{"name":"a.txt","dir":false,"size":3,"modified":"2024-01-02T00:00:00Z"}]"""));
        await using var _db = db;
        var r = await svc.ListAsync(Win, @"C:\Temp");
        Assert.Equal("a.txt", r.Value!.Entries.Single().Name);
        Assert.Equal("runscript", bus.Sent.Single()["func"]);
        var payload = (IDictionary<string, object?>)bus.Sent[0]["payload"]!;
        Assert.Equal("powershell", payload["shell"]); Assert.Contains(@"'C:\Temp'", (string)payload["code"]!);
    }

    [PostgresFact]
    public async Task BadInputNeverReachesTheAgent()
    {
        var (svc, bus, _, _, db) = await SetupAsync(_ => Out("@@OK@@"));
        await using var _db = db;
        Assert.Equal(OutcomeStatus.Invalid, (await svc.ListAsync(Win, "relative")).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.ListAsync(Lin, @"C:\")).Status);                      // Windows only
        Assert.Equal(OutcomeStatus.NotFound, (await svc.ListAsync("nope-nope-nope-nope-1", @"C:\")).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.DeleteAsync(Caller, Win, new FileDeleteRequest(@"C:\", true))).Status);   // a drive root
        Assert.Equal(OutcomeStatus.Invalid, (await svc.NewFolderAsync(Caller, Win, new FolderRequest(@"C:\", "..\\x"))).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.RenameAsync(Caller, Win, new FileRenameRequest(@"C:\a", "b\\c"))).Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.UploadAsync(Caller, Win, new FileUploadRequest(@"C:\d", "x.txt", "%%%"))).Status);
        Assert.Empty(bus.Sent);
    }

    [PostgresFact]
    public async Task ChangesAreAuditedAndAddedToTheHistory()
    {
        var (svc, bus, audit, log, db) = await SetupAsync(_ => Out("@@OK@@"));
        await using var _db = db;
        Assert.True((await svc.NewFolderAsync(Caller, Win, new FolderRequest(@"C:\Temp", "new"))).IsOk);
        Assert.True((await svc.DeleteAsync(Caller, Win, new FileDeleteRequest(@"C:\Temp\old", true))).IsOk);
        Assert.True((await svc.RenameAsync(Caller, Win, new FileRenameRequest(@"C:\Temp\a", "b"))).IsOk);
        Assert.Equal(3, bus.Sent.Count);
        Assert.Contains(audit.Messages, m => m.Contains(@"deleted C:\Temp\old") && m.Contains("WIN1"));
        Assert.Contains(log.Commands, c => c.StartsWith("[files]") && c.Contains("created folder"));
    }

    [PostgresFact]
    public async Task ADownloadReturnsTheDecodedBytesAndUploadSendsTheContent()
    {
        var (svc, bus, _, _, db) = await SetupAsync(d => ((string)((IDictionary<string, object?>)d["payload"]!)["code"]!).Contains("ReadAllBytes") ? Out("@@OK@@QUJD") : Out("@@OK@@3"));
        await using var _db = db;
        var d = await svc.DownloadAsync(Caller, Win, @"C:\Temp\f.txt");
        Assert.Equal("f.txt", d.Value!.FileName); Assert.Equal("ABC"u8.ToArray(), d.Value.Content);
        var u = await svc.UploadAsync(Caller, Win, new FileUploadRequest(@"C:\Temp", "f.txt", "QUJD", true));
        Assert.True(u.IsOk);
        Assert.Contains("'QUJD'", (string)((IDictionary<string, object?>)bus.Sent[1]["payload"]!)["code"]!);
    }

    [PostgresFact]
    public async Task AnAgentErrorOrTimeoutIsReportedNotHidden()
    {
        var (svc, _, _, _, db) = await SetupAsync(_ => Out("@@ERR@@ Access to the path is denied."));
        await using var _db = db;
        var r = await svc.DeleteAsync(Caller, Win, new FileDeleteRequest(@"C:\x\y", false));
        Assert.Equal(OutcomeStatus.Invalid, r.Status); Assert.Contains("Access to the path is denied.", r.Error);
        var (svc2, _, _, _, db2) = await SetupAsync(_ => "timeout");
        await using var _db2 = db2;
        Assert.Equal(OutcomeStatus.Conflict, (await svc2.ListAsync(Win, "")).Status);
    }
}
