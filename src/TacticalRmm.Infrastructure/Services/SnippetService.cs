using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class SnippetService(AppDbContext db, IAuditService audit) : ISnippetService
{
    public async Task<Outcome<IReadOnlyList<SnippetDto>>> ListAsync(CancellationToken ct = default)
    {
        var snippets = await db.ScriptSnippets.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var bodies = await db.Scripts.AsNoTracking().Select(s => s.ScriptBody).ToListAsync(ct);
        var used = bodies.SelectMany(SnippetRules.Names).GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        return Outcome.Ok<IReadOnlyList<SnippetDto>>(snippets.Select(s => new SnippetDto(s.Id, s.Name, s.Description, null, used.GetValueOrDefault(s.Name))).ToList());
    }

    public async Task<Outcome<SnippetDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var s = await db.ScriptSnippets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null ? Outcome.NotFound<SnippetDto>() : Outcome.Ok(new SnippetDto(s.Id, s.Name, s.Description, s.Code, 0));
    }

    public async Task<Outcome<SnippetDto>> CreateAsync(User caller, SnippetUpsert req, CancellationToken ct = default)
    {
        var err = SnippetRules.CheckName(req.Name) ?? SnippetRules.CheckCode(req.Code);
        if (err is not null) return Outcome.Invalid<SnippetDto>(err);
        var name = req.Name!.Trim();
        if (await db.ScriptSnippets.AnyAsync(x => x.Name == name, ct)) return Outcome.Conflict<SnippetDto>("A snippet with this name already exists");
        var s = new ScriptSnippet { Name = name, Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(), Code = req.Code!, CreatedBy = caller.Username, CreatedTime = DateTime.UtcNow };
        db.ScriptSnippets.Add(s);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "snippet", $"{caller.Username} created script snippet '{name}'", ct: ct);
        return Outcome.Ok(new SnippetDto(s.Id, s.Name, s.Description, s.Code, 0));
    }

    public async Task<Outcome<SnippetDto>> UpdateAsync(User caller, int id, SnippetUpsert req, CancellationToken ct = default)
    {
        var s = await db.ScriptSnippets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Outcome.NotFound<SnippetDto>();
        if (req.Name is not null && req.Name.Trim() != s.Name)
        {
            // Scripts refer to a snippet by name, so a rename would silently break them.
            return Outcome.Conflict<SnippetDto>("A snippet cannot be renamed because scripts refer to it by name; create a new one instead");
        }
        if (req.Code is not null)
        {
            var err = SnippetRules.CheckCode(req.Code);
            if (err is not null) return Outcome.Invalid<SnippetDto>(err);
            s.Code = req.Code;
        }
        if (req.Description is not null) s.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        s.ModifiedBy = caller.Username; s.ModifiedTime = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "snippet", $"{caller.Username} modified script snippet '{s.Name}'", ct: ct);
        return Outcome.Ok(new SnippetDto(s.Id, s.Name, s.Description, s.Code, 0));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var s = await db.ScriptSnippets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Outcome.NotFound<bool>();
        var bodies = await db.Scripts.AsNoTracking().Select(x => x.ScriptBody).ToListAsync(ct);
        var users = bodies.Count(b => SnippetRules.Names(b).Contains(s.Name));
        if (users > 0) return Outcome.Conflict<bool>($"{users} script(s) still use this snippet; remove {{{{{s.Name}}}}} from them first");
        db.ScriptSnippets.Remove(s);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "snippet", $"{caller.Username} deleted script snippet '{s.Name}'", ct: ct);
        return Outcome.Ok(true);
    }
}

/// <summary>Puts snippet code into a script body just before the script is sent to an agent.</summary>
public static class SnippetExpander
{
    /// <summary>Loads only the snippets the given bodies refer to.</summary>
    public static async Task<Dictionary<string, string>> LoadAsync(AppDbContext db, IEnumerable<string?> bodies, CancellationToken ct = default)
    {
        var names = bodies.SelectMany(SnippetRules.Names).Distinct().ToList();
        if (names.Count == 0) return new();
        return await db.ScriptSnippets.AsNoTracking().Where(s => names.Contains(s.Name)).ToDictionaryAsync(s => s.Name, s => s.Code, ct);
    }

    public static async Task<string> ExpandAsync(AppDbContext db, string body, CancellationToken ct = default)
        => SnippetRules.Expand(body, await LoadAsync(db, [body], ct)).Body;
}
