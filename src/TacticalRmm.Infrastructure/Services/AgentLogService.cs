using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class AgentLogService(AppDbContext db, IAuditService audit, ILogger<AgentLogService> log) : IAgentLogService
{
    public async Task<Outcome<IReadOnlyList<HistoryEntryDto>>> HistoryAsync(string agentId, int? take, CancellationToken ct = default)
    {
        var id = await db.Agents.AsNoTracking().Where(a => a.AgentId == agentId).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        if (id is null) return Outcome.NotFound<IReadOnlyList<HistoryEntryDto>>("Agent not found");
        var rows = await db.AgentHistory.AsNoTracking().Where(h => h.AgentId == id).OrderByDescending(h => h.Time).ThenByDescending(h => h.Id).Take(AgentLogRules.Take(take)).ToListAsync(ct);
        IReadOnlyList<HistoryEntryDto> list = rows.Select(h =>
        {
            var (o, e, rc, ex) = AgentLogRules.ParseResults(h.ScriptResults);
            return new HistoryEntryDto(h.Id, h.Time, h.Type, h.Command, h.Username, o, e, rc, ex);
        }).ToList();
        return Outcome.Ok(list);
    }

    public async Task<Outcome<IReadOnlyList<NoteDto>>> NotesAsync(string agentId, CancellationToken ct = default)
    {
        var id = await db.Agents.AsNoTracking().Where(a => a.AgentId == agentId).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        if (id is null) return Outcome.NotFound<IReadOnlyList<NoteDto>>("Agent not found");
        var rows = await (from n in db.AgentNotes.AsNoTracking().Where(n => n.AgentId == id)
                          join u in db.Users on n.UserId equals u.Id into us
                          from u in us.DefaultIfEmpty()
                          orderby n.EntryTime descending, n.Id descending
                          select new NoteDto(n.Id, n.Note ?? "", u == null ? null : u.Username, n.EntryTime)).Take(AgentLogRules.MaxNotes).ToListAsync(ct);
        return Outcome.Ok<IReadOnlyList<NoteDto>>(rows);
    }

    public async Task<Outcome<NoteDto>> AddNoteAsync(User caller, string agentId, string? text, CancellationToken ct = default)
    {
        var note = AgentLogRules.CleanNote(text);
        if (note is null) return Outcome.Invalid<NoteDto>("Write something first.");
        var a = await db.Agents.AsNoTracking().Where(x => x.AgentId == agentId).Select(x => new { x.Id, x.Hostname }).FirstOrDefaultAsync(ct);
        if (a is null) return Outcome.NotFound<NoteDto>("Agent not found");
        var n = new AgentNote { AgentId = a.Id, UserId = caller.Id, Note = note, EntryTime = DateTime.UtcNow };
        db.AgentNotes.Add(n);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "agent", $"{caller.Username} added a note to {a.Hostname}", a.Hostname, a.Id, ct: ct);
        return Outcome.Ok(new NoteDto(n.Id, note, caller.Username, n.EntryTime));
    }

    public async Task<Outcome<bool>> DeleteNoteAsync(User caller, string agentId, int noteId, CancellationToken ct = default)
    {
        var a = await db.Agents.AsNoTracking().Where(x => x.AgentId == agentId).Select(x => new { x.Id, x.Hostname }).FirstOrDefaultAsync(ct);
        var n = a is null ? null : await db.AgentNotes.FirstOrDefaultAsync(x => x.Id == noteId && x.AgentId == a.Id, ct);   // the note must belong to this asset
        if (a is null || n is null) return Outcome.NotFound<bool>("Note not found");
        db.AgentNotes.Remove(n);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "agent", $"{caller.Username} deleted a note of {a.Hostname}", a.Hostname, a.Id, ct: ct);
        return Outcome.Ok(true);
    }

    public async Task RecordCommandAsync(string agentId, string username, string command, CancellationToken ct = default)
    {
        try
        {
            var id = await db.Agents.AsNoTracking().Where(a => a.AgentId == agentId).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
            if (id is null) return;
            db.AgentHistory.Add(new AgentHistory { AgentId = id.Value, Time = DateTime.UtcNow, Type = "cmd_run", Command = AgentLogRules.CommandText(command), Username = username[..Math.Min(50, username.Length)] });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "could not record the command in the history of {Agent}", agentId); }
    }
}
