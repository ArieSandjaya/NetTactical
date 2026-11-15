using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class AlertTemplateAdminService(AppDbContext db, IAuditService audit) : IAlertTemplateAdminService
{
    private static string? CheckName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 ? "name is required (max 100 characters)" : null;

    public async Task<Outcome<IReadOnlyList<AlertTemplateDto>>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.AlertTemplates.AsNoTracking().OrderBy(t => t.Name).Select(t => new AlertTemplateDto(
            t.Id, t.Name, t.Enabled, t.IsDefault, t.FlagAgentOverdue, t.FlagCheckFailures, t.FlagTaskFailures,
            db.Clients.Count(c => c.AlertTemplateId == t.Id),
            db.Sites.Count(s => s.AlertTemplateId == t.Id))).ToListAsync(ct);
        return Outcome.Ok<IReadOnlyList<AlertTemplateDto>>(rows);
    }

    public async Task<Outcome<AlertTemplateDto>> CreateAsync(User caller, AlertTemplateUpsert req, CancellationToken ct = default)
    {
        var err = CheckName(req.Name);
        if (err is not null) return Outcome.Invalid<AlertTemplateDto>(err);
        var name = req.Name!.Trim();
        if (await db.AlertTemplates.AnyAsync(t => t.Name == name, ct)) return Outcome.Conflict<AlertTemplateDto>("An alert template with this name already exists");
        var t = new AlertTemplate
        {
            Name = name, Enabled = req.Enabled ?? true, IsDefault = req.IsDefault ?? false,
            FlagAgentOverdue = req.FlagAgentOverdue ?? false, FlagCheckFailures = req.FlagCheckFailures ?? false, FlagTaskFailures = req.FlagTaskFailures ?? false,
            CreatedBy = caller.Username, CreatedTime = DateTime.UtcNow,
        };
        if (t.IsDefault) await ClearOtherDefaultsAsync(0, ct);
        db.AlertTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "alerttemplate", $"{caller.Username} created alert template '{name}'", ct: ct);
        return Outcome.Ok(new AlertTemplateDto(t.Id, t.Name, t.Enabled, t.IsDefault, t.FlagAgentOverdue, t.FlagCheckFailures, t.FlagTaskFailures, 0, 0));
    }

    public async Task<Outcome<AlertTemplateDto>> UpdateAsync(User caller, int id, AlertTemplateUpsert req, CancellationToken ct = default)
    {
        var t = await db.AlertTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return Outcome.NotFound<AlertTemplateDto>();
        if (req.Name is not null)
        {
            var err = CheckName(req.Name);
            if (err is not null) return Outcome.Invalid<AlertTemplateDto>(err);
            var name = req.Name.Trim();
            if (await db.AlertTemplates.AnyAsync(x => x.Name == name && x.Id != id, ct)) return Outcome.Conflict<AlertTemplateDto>("An alert template with this name already exists");
            t.Name = name;
        }
        if (req.Enabled is { } en) t.Enabled = en;
        if (req.IsDefault is { } def)
        {
            if (def) await ClearOtherDefaultsAsync(id, ct);
            t.IsDefault = def;
        }
        if (req.FlagAgentOverdue is { } a) t.FlagAgentOverdue = a;
        if (req.FlagCheckFailures is { } c) t.FlagCheckFailures = c;
        if (req.FlagTaskFailures is { } k) t.FlagTaskFailures = k;
        t.ModifiedBy = caller.Username; t.ModifiedTime = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "alerttemplate", $"{caller.Username} modified alert template '{t.Name}'", ct: ct);
        var list = await ListAsync(ct);
        return Outcome.Ok(list.Value!.First(x => x.Id == id));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var t = await db.AlertTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return Outcome.NotFound<bool>();
        // Clients and sites that used it fall back to the next level (their foreign key is set to null by the database).
        db.AlertTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "alerttemplate", $"{caller.Username} deleted alert template '{t.Name}'", ct: ct);
        return Outcome.Ok(true);
    }

    /// <summary>Only one template can be the default; the change is saved together with the caller's own save.</summary>
    private async Task ClearOtherDefaultsAsync(int exceptId, CancellationToken ct)
    {
        var others = await db.AlertTemplates.Where(x => x.IsDefault && x.Id != exceptId).ToListAsync(ct);
        foreach (var o in others) o.IsDefault = false;
    }
}
