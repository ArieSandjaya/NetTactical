using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class CustomFieldService(AppDbContext db, IAuditService audit) : ICustomFieldService
{
    public async Task<Outcome<IReadOnlyList<CustomFieldDto>>> ListAsync(string? model, CancellationToken ct = default)
    {
        if (model is not null && !CustomFieldRules.Models.Contains(model)) return Outcome.Invalid<IReadOnlyList<CustomFieldDto>>("model must be client, site or agent");
        var rows = await db.CustomFields.AsNoTracking().Where(f => model == null || f.Model == model)
            .OrderBy(f => f.Model).ThenBy(f => f.Name)
            .Select(f => new CustomFieldDto(f.Id, f.Name, f.Model, f.Type, f.Options, f.DefaultValue, db.CustomFieldValues.Count(v => v.FieldId == f.Id))).ToListAsync(ct);
        return Outcome.Ok<IReadOnlyList<CustomFieldDto>>(rows);
    }

    public async Task<Outcome<CustomFieldDto>> CreateAsync(User caller, CustomFieldUpsert req, CancellationToken ct = default)
    {
        var err = CustomFieldRules.CheckDefinition(req.Name, req.Model, req.Type, req.Options, req.DefaultValue);
        if (err is not null) return Outcome.Invalid<CustomFieldDto>(err);
        var name = req.Name!.Trim();
        if (await db.CustomFields.AnyAsync(f => f.Model == req.Model && f.Name == name, ct)) return Outcome.Conflict<CustomFieldDto>($"A {req.Model} field with this name already exists");
        var f = new CustomField
        {
            Name = name, Model = req.Model!, Type = req.Type!,
            Options = req.Type == "single" ? string.Join("\n", CustomFieldRules.ParseOptions(req.Options)) : null,
            DefaultValue = Default(req.Type!, req.Options, req.DefaultValue),
            CreatedBy = caller.Username, CreatedTime = DateTime.UtcNow,
        };
        db.CustomFields.Add(f);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "customfield", $"{caller.Username} created {f.Model} custom field '{f.Name}'", ct: ct);
        return Outcome.Ok(new CustomFieldDto(f.Id, f.Name, f.Model, f.Type, f.Options, f.DefaultValue, 0));
    }

    public async Task<Outcome<CustomFieldDto>> UpdateAsync(User caller, int id, CustomFieldUpsert req, CancellationToken ct = default)
    {
        var f = await db.CustomFields.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return Outcome.NotFound<CustomFieldDto>();
        if ((req.Model is not null && req.Model != f.Model) || (req.Type is not null && req.Type != f.Type))
            return Outcome.Conflict<CustomFieldDto>("The model and type of a field cannot be changed; create a new field instead");
        var name = req.Name is null ? f.Name : req.Name.Trim();
        var options = f.Type == "single" ? (req.Options ?? f.Options) : null;
        var def = req.DefaultValue is null ? f.DefaultValue : req.DefaultValue;
        var err = CustomFieldRules.CheckDefinition(name, f.Model, f.Type, options, def);
        if (err is not null) return Outcome.Invalid<CustomFieldDto>(err);
        if (await db.CustomFields.AnyAsync(x => x.Model == f.Model && x.Name == name && x.Id != id, ct)) return Outcome.Conflict<CustomFieldDto>($"A {f.Model} field with this name already exists");
        f.Name = name;
        if (f.Type == "single")
        {
            var keep = CustomFieldRules.ParseOptions(options);
            f.Options = string.Join("\n", keep);
            // A value that is no longer a choice would never be valid again: drop it so the default applies.
            var stale = await db.CustomFieldValues.Where(v => v.FieldId == id && !keep.Contains(v.Value)).ToListAsync(ct);
            db.CustomFieldValues.RemoveRange(stale);
        }
        f.DefaultValue = Default(f.Type, f.Options, def);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "customfield", $"{caller.Username} modified {f.Model} custom field '{f.Name}'", ct: ct);
        var list = await ListAsync(f.Model, ct);
        return Outcome.Ok(list.Value!.First(x => x.Id == id));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var f = await db.CustomFields.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return Outcome.NotFound<bool>();
        db.CustomFields.Remove(f);   // its values go with it (cascade)
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "customfield", $"{caller.Username} deleted {f.Model} custom field '{f.Name}'", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<IReadOnlyList<CustomFieldValueDto>>> ValuesAsync(User caller, string model, string target, CancellationToken ct = default)
    {
        var t = await ResolveAsync(caller, model, target, write: false, ct);
        if (!t.IsOk) return Outcome.Fail<IReadOnlyList<CustomFieldValueDto>>(t);
        return Outcome.Ok(await BuildAsync(model, t.Value, ct));
    }

    public async Task<Outcome<IReadOnlyList<CustomFieldValueDto>>> SetValuesAsync(User caller, string model, string target, CustomFieldValuesSet req, CancellationToken ct = default)
    {
        var t = await ResolveAsync(caller, model, target, write: true, ct);
        if (!t.IsOk) return Outcome.Fail<IReadOnlyList<CustomFieldValueDto>>(t);
        var targetId = t.Value;
        var ids = req.Values.Keys.ToList();
        var fields = await db.CustomFields.Where(f => f.Model == model && ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var normalised = new Dictionary<int, string>();
        foreach (var (fid, raw) in req.Values)
        {
            if (!fields.TryGetValue(fid, out var f)) return Outcome.Invalid<IReadOnlyList<CustomFieldValueDto>>($"Unknown {model} field {fid}");
            var (ok, value, err) = CustomFieldRules.Normalize(f.Type, f.Options, raw);
            if (!ok) return Outcome.Invalid<IReadOnlyList<CustomFieldValueDto>>($"{f.Name}: {err}");
            normalised[fid] = value;
        }
        var existing = await db.CustomFieldValues.Where(v => v.TargetId == targetId && ids.Contains(v.FieldId)).ToDictionaryAsync(v => v.FieldId, ct);
        foreach (var (fid, value) in normalised)
        {
            existing.TryGetValue(fid, out var row);
            if (value.Length == 0) { if (row is not null) db.CustomFieldValues.Remove(row); }
            else if (row is null) db.CustomFieldValues.Add(new CustomFieldValue { FieldId = fid, TargetId = targetId, Value = value });
            else row.Value = value;
        }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, model, $"{caller.Username} changed custom fields ({string.Join(", ", normalised.Keys.Select(k => fields[k].Name))}) of {model} {target}", ct: ct);
        return Outcome.Ok(await BuildAsync(model, targetId, ct));
    }

    private static string? Default(string type, string? options, string? raw)
    {
        var (_, v, _) = CustomFieldRules.Normalize(type, options, raw);
        return v.Length == 0 ? null : v;
    }

    private async Task<IReadOnlyList<CustomFieldValueDto>> BuildAsync(string model, int targetId, CancellationToken ct)
    {
        var fields = await db.CustomFields.AsNoTracking().Where(f => f.Model == model).OrderBy(f => f.Name).ToListAsync(ct);
        var values = await db.CustomFieldValues.AsNoTracking().Where(v => v.TargetId == targetId && v.Field.Model == model).ToDictionaryAsync(v => v.FieldId, v => v.Value, ct);
        return fields.Select(f =>
        {
            var v = values.GetValueOrDefault(f.Id, "");
            return new CustomFieldValueDto(f.Id, f.Name, f.Type, f.Options, f.DefaultValue, v, v.Length > 0 ? v : f.DefaultValue ?? "");
        }).ToList();
    }

    /// <summary>Checks the model, the caller's permission and client/site scope, and returns the target's database id.</summary>
    private async Task<Outcome<int>> ResolveAsync(User caller, string model, string target, bool write, CancellationToken ct)
    {
        switch (model)
        {
            case "client":
                if (!PermissionEvaluator.Has(caller, write ? Perm.ManageClients : Perm.ListClients)) return Outcome.Forbidden<int>();
                if (!int.TryParse(target, out var cid) || !await db.Clients.AnyAsync(c => c.Id == cid, ct)) return Outcome.NotFound<int>();
                return ScopeGuard.CanSeeClient(caller, cid) ? Outcome.Ok(cid) : Outcome.Forbidden<int>();
            case "site":
                if (!PermissionEvaluator.Has(caller, write ? Perm.ManageSites : Perm.ListSites)) return Outcome.Forbidden<int>();
                if (!int.TryParse(target, out var sid)) return Outcome.NotFound<int>();
                var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct);
                if (site is null) return Outcome.NotFound<int>();
                return PermissionEvaluator.CanSeeSite(caller, site) ? Outcome.Ok(sid) : Outcome.Forbidden<int>();
            case "agent":
                if (!PermissionEvaluator.Has(caller, write ? Perm.EditAgent : Perm.ListAgents)) return Outcome.Forbidden<int>();
                var agent = await db.Agents.AsNoTracking().Include(a => a.Site).FirstOrDefaultAsync(a => a.AgentId == target, ct);
                if (agent is null) return Outcome.NotFound<int>();
                return ScopeGuard.CanSeeAgent(caller, agent) ? Outcome.Ok(agent.Id) : Outcome.Forbidden<int>();
            default:
                return Outcome.Invalid<int>("model must be client, site or agent");
        }
    }
}
