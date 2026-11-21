using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class FileBrowserService(AppDbContext db, IAgentCommandBus bus, IAuditService audit, IAgentLogService agentLog) : IFileBrowserService
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(30), Long = TimeSpan.FromSeconds(120);

    private async Task<(int Id, string Hostname, string Plat)?> FindAsync(string agentId, CancellationToken ct)
    {
        var a = await db.Agents.AsNoTracking().Where(x => x.AgentId == agentId).Select(x => new { x.Id, x.Hostname, x.Plat }).FirstOrDefaultAsync(ct);
        return a is null ? null : (a.Id, a.Hostname, a.Plat ?? "");
    }

    private Task<(string? Payload, string? Error)> RunAsync(string agentId, string script, TimeSpan timeout, CancellationToken ct) => AgentScripts.RunAsync(bus, agentId, script, timeout, ct);

    private static Outcome<T>? NotWindows<T>(string plat) =>
        string.Equals(plat, "windows", StringComparison.OrdinalIgnoreCase) ? null : Outcome.Invalid<T>("The file browser is only available for Windows assets.");

    private static Outcome<T> Fail<T>(string error) => error.Contains("did not answer") || error.Contains("NATS") ? Outcome.Conflict<T>(error) : Outcome.Invalid<T>(error);

    private async Task TraceAsync(User caller, (int Id, string Hostname, string Plat) a, string agentId, string what, CancellationToken ct)
    {
        await audit.LogAsync(caller.Username, AuditAction.ExecCommand, "agent", $"{caller.Username} {what} on {a.Hostname} (file browser)", a.Hostname, a.Id, ct: ct);
        await agentLog.RecordCommandAsync(agentId, caller.Username, $"[files] {what}", ct);
    }

    public async Task<Outcome<FileListDto>> ListAsync(string agentId, string? path, CancellationToken ct = default)
    {
        path ??= "";
        if (!FileBrowser.ValidPath(path, allowEmpty: true)) return Outcome.Invalid<FileListDto>("That is not a valid path. Use a drive path such as C:\\Users or a network path such as \\\\server\\share.");
        if (await FindAsync(agentId, ct) is not { } a) return Outcome.NotFound<FileListDto>("Agent not found");
        if (NotWindows<FileListDto>(a.Plat) is { } nw) return nw;
        var (payload, err) = await RunAsync(agentId, FileBrowser.ListScript(path), Short, ct);
        if (err is not null) return Fail<FileListDto>(err);
        return FileBrowser.ParseList(path, payload!) is { } list ? Outcome.Ok(list) : Outcome.Invalid<FileListDto>("The agent sent a list that could not be read.");
    }

    public async Task<Outcome<FileDownloadDto>> DownloadAsync(User caller, string agentId, string? path, CancellationToken ct = default)
    {
        if (!FileBrowser.ValidPath(path) || FileBrowser.IsRoot(path!)) return Outcome.Invalid<FileDownloadDto>("That is not a valid file path.");
        if (await FindAsync(agentId, ct) is not { } a) return Outcome.NotFound<FileDownloadDto>("Agent not found");
        if (NotWindows<FileDownloadDto>(a.Plat) is { } nw) return nw;
        await TraceAsync(caller, a, agentId, $"downloaded {path}", ct);
        var (payload, err) = await RunAsync(agentId, FileBrowser.DownloadScript(path!), Long, ct);
        if (err is not null) return Fail<FileDownloadDto>(err);
        return FileBrowser.ParseBase64(payload!) is { } bytes ? Outcome.Ok(new FileDownloadDto(FileBrowser.DownloadName(path!), bytes)) : Outcome.Invalid<FileDownloadDto>("The agent sent a file that could not be read.");
    }

    public async Task<Outcome<ToolResult>> UploadAsync(User caller, string agentId, FileUploadRequest req, CancellationToken ct = default)
    {
        if (!FileBrowser.ValidPath(req.Path) || !FileBrowser.ValidName(req.Name)) return Outcome.Invalid<ToolResult>("A valid target folder and file name are required.");
        if (string.IsNullOrEmpty(req.ContentBase64)) return Outcome.Invalid<ToolResult>("The file is empty.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(req.ContentBase64); } catch (FormatException) { return Outcome.Invalid<ToolResult>("The file content is not valid base64."); }
        if (bytes.Length > FileBrowser.MaxUploadBytes) return Outcome.Invalid<ToolResult>($"The file is larger than {FileBrowser.MaxUploadBytes / 1024 / 1024} MB.");
        if (await FindAsync(agentId, ct) is not { } a) return Outcome.NotFound<ToolResult>("Agent not found");
        if (NotWindows<ToolResult>(a.Plat) is { } nw) return nw;
        await TraceAsync(caller, a, agentId, $"uploaded {FileBrowser.Combine(req.Path!, req.Name!)} ({bytes.Length} bytes)", ct);
        var (payload, err) = await RunAsync(agentId, FileBrowser.UploadScript(req.Path!, req.Name!, req.ContentBase64, req.Overwrite), Long, ct);
        return err is not null ? Fail<ToolResult>(err) : Outcome.Ok(new ToolResult($"Uploaded {req.Name} ({payload} bytes)."));
    }

    public async Task<Outcome<ToolResult>> NewFolderAsync(User caller, string agentId, FolderRequest req, CancellationToken ct = default)
    {
        if (!FileBrowser.ValidPath(req.Path) || !FileBrowser.ValidName(req.Name)) return Outcome.Invalid<ToolResult>("A valid parent folder and folder name are required.");
        if (await FindAsync(agentId, ct) is not { } a) return Outcome.NotFound<ToolResult>("Agent not found");
        if (NotWindows<ToolResult>(a.Plat) is { } nw) return nw;
        await TraceAsync(caller, a, agentId, $"created folder {FileBrowser.Combine(req.Path!, req.Name!)}", ct);
        var (_, err) = await RunAsync(agentId, FileBrowser.MkdirScript(req.Path!, req.Name!), Short, ct);
        return err is not null ? Fail<ToolResult>(err) : Outcome.Ok(new ToolResult("Folder created."));
    }

    public async Task<Outcome<ToolResult>> DeleteAsync(User caller, string agentId, FileDeleteRequest req, CancellationToken ct = default)
    {
        if (!FileBrowser.ValidPath(req.Path)) return Outcome.Invalid<ToolResult>("That is not a valid path.");
        if (FileBrowser.IsRoot(req.Path!)) return Outcome.Invalid<ToolResult>("A drive or share root cannot be deleted.");
        if (await FindAsync(agentId, ct) is not { } a) return Outcome.NotFound<ToolResult>("Agent not found");
        if (NotWindows<ToolResult>(a.Plat) is { } nw) return nw;
        await TraceAsync(caller, a, agentId, $"deleted {req.Path}{(req.Recursive ? " (with contents)" : "")}", ct);
        var (_, err) = await RunAsync(agentId, FileBrowser.DeleteScript(req.Path!, req.Recursive), Long, ct);
        return err is not null ? Fail<ToolResult>(err) : Outcome.Ok(new ToolResult("Deleted."));
    }

    public async Task<Outcome<ToolResult>> RenameAsync(User caller, string agentId, FileRenameRequest req, CancellationToken ct = default)
    {
        if (!FileBrowser.ValidPath(req.Path) || FileBrowser.IsRoot(req.Path!) || !FileBrowser.ValidName(req.NewName)) return Outcome.Invalid<ToolResult>("A valid path and a new name are required.");
        if (await FindAsync(agentId, ct) is not { } a) return Outcome.NotFound<ToolResult>("Agent not found");
        if (NotWindows<ToolResult>(a.Plat) is { } nw) return nw;
        await TraceAsync(caller, a, agentId, $"renamed {req.Path} to {req.NewName}", ct);
        var (_, err) = await RunAsync(agentId, FileBrowser.RenameScript(req.Path!, req.NewName!), Short, ct);
        return err is not null ? Fail<ToolResult>(err) : Outcome.Ok(new ToolResult("Renamed."));
    }
}
