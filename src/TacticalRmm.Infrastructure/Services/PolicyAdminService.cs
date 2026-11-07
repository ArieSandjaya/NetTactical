using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class PolicyAdminService(AppDbContext db, IAuditService audit) : IPolicyAdminService
{
    public async Task<Outcome<IReadOnlyList<PolicyDto>>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.Policies.AsNoTracking().OrderBy(p => p.Name).Select(p => new PolicyDto(
            p.Id, p.Name, p.Desc, p.Active, p.Enforced,
            db.Agents.Count(a => a.PolicyId == p.Id),
            db.Checks.Count(c => c.PolicyId == p.Id),
            db.AutomatedTasks.Count(t => t.PolicyId == p.Id && !t.PendingDeletion))).ToListAsync(ct);
        return Outcome.Ok<IReadOnlyList<PolicyDto>>(rows);
    }

    public async Task<Outcome<PolicyDto>> CreateAsync(User caller, PolicyUpsert req, CancellationToken ct = default)
    {
        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 255) return Outcome.Invalid<PolicyDto>("name is required (max 255 characters)");
        if (await db.Policies.AnyAsync(p => p.Name == name, ct)) return Outcome.Conflict<PolicyDto>("A policy with this name already exists");
        var p = new Policy { Name = name, Desc = req.Desc, Active = req.Active ?? false, Enforced = req.Enforced ?? false };
        db.Policies.Add(p);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "policy", $"{caller.Username} created policy '{name}'", ct: ct);
        return Outcome.Ok(new PolicyDto(p.Id, p.Name, p.Desc, p.Active, p.Enforced, 0, 0, 0));
    }

    public async Task<Outcome<PolicyDto>> UpdateAsync(User caller, int id, PolicyUpsert req, CancellationToken ct = default)
    {
        var p = await db.Policies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Outcome.NotFound<PolicyDto>();
        if (req.Name is not null)
        {
            var name = req.Name.Trim();
            if (name.Length is 0 or > 255) return Outcome.Invalid<PolicyDto>("name is required (max 255 characters)");
            if (await db.Policies.AnyAsync(x => x.Name == name && x.Id != id, ct)) return Outcome.Conflict<PolicyDto>("A policy with this name already exists");
            p.Name = name;
        }
        if (req.Desc is not null) p.Desc = req.Desc;
        var wasActive = p.Active;
        if (req.Active is { } a) p.Active = a;
        if (req.Enforced is { } e) p.Enforced = e;
        await db.SaveChangesAsync(ct);

        // Policy baru diaktifkan -> tandai ulang task-nya agar ikut tersinkron ke agent oleh TaskSyncJob.
        if (!wasActive && p.Active)
        {
            var results = await db.TaskResults.Where(r => r.Task.PolicyId == id).ToListAsync(ct);
            foreach (var r in results) r.SyncStatus = TaskSyncStatus.NotSynced;
            await db.SaveChangesAsync(ct);
        }
        await audit.LogAsync(caller.Username, AuditAction.Modify, "policy", $"{caller.Username} modified policy '{p.Name}'", ct: ct);
        var list = await ListAsync(ct);
        return Outcome.Ok(list.Value!.First(x => x.Id == id));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var p = await db.Policies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Outcome.NotFound<bool>();
        // Task policy sudah tersebar di Task Scheduler agent: hapus dulu lewat DELETE task agar bersih di sisi agent.
        var tasks = await db.AutomatedTasks.CountAsync(t => t.PolicyId == id, ct);
        if (tasks > 0) return Outcome.Conflict<bool>($"Policy still has {tasks} task(s); delete them first so agents are cleaned up");

        foreach (var a in await db.Agents.Where(a => a.PolicyId == id).ToListAsync(ct)) a.PolicyId = null;
        db.Checks.RemoveRange(db.Checks.Where(c => c.PolicyId == id));
        db.Policies.Remove(p);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "policy", $"{caller.Username} deleted policy '{p.Name}'", ct: ct);
        return Outcome.Ok(true);
    }
}
