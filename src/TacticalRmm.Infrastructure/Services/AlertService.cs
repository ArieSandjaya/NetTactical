using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>Alert dashboard. Notifikasi email/SMS/webhook (AlertTemplate) belum termasuk cakupan inti.</summary>
public sealed class AlertService(AppDbContext db) : IAlertService
{
    private async Task<Alert> OpenOrCreate(Agent agent, string type, int? checkId, int? taskId, string msg, string severity, CancellationToken ct)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.AgentId == agent.Id && a.AlertType == type && !a.Resolved
            && a.AssignedCheckId == checkId && a.AssignedTaskId == taskId, ct);
        if (alert is not null) { alert.Severity = severity; await db.SaveChangesAsync(ct); return alert; }
        alert = new Alert { AgentId = agent.Id, AlertType = type, AssignedCheckId = checkId, AssignedTaskId = taskId,
            Message = msg, Severity = severity, AlertTime = DateTime.UtcNow };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);
        return alert;
    }

    private async Task Resolve(Agent agent, string type, int? checkId, int? taskId, CancellationToken ct)
    {
        var open = await db.Alerts.Where(a => a.AgentId == agent.Id && a.AlertType == type && !a.Resolved
            && a.AssignedCheckId == checkId && a.AssignedTaskId == taskId).ToListAsync(ct);
        foreach (var a in open) { a.Resolved = true; a.ResolvedOn = DateTime.UtcNow; }
        if (open.Count > 0) await db.SaveChangesAsync(ct);
    }

    public Task RaiseCheckFailureAsync(Agent agent, Check check, CheckResult r, CancellationToken ct = default) =>
        OpenOrCreate(agent, AlertType.Check, check.Id, null, $"{agent.Hostname} - {check.Name ?? check.CheckType} Failed", r.AlertSeverity ?? check.AlertSeverity ?? AlertSeverity.Warning, ct);
    public Task ResolveCheckAsync(Agent agent, Check check, CancellationToken ct = default) => Resolve(agent, AlertType.Check, check.Id, null, ct);

    public Task RaiseTaskFailureAsync(Agent agent, AutomatedTask task, TaskResult r, CancellationToken ct = default) =>
        OpenOrCreate(agent, AlertType.Task, null, task.Id, $"{agent.Hostname} - {task.Name} Failed", task.AlertSeverity, ct);
    public Task ResolveTaskAsync(Agent agent, AutomatedTask task, CancellationToken ct = default) => Resolve(agent, AlertType.Task, null, task.Id, ct);

    public Task RaiseAvailabilityAsync(Agent agent, CancellationToken ct = default) =>
        OpenOrCreate(agent, AlertType.Availability, null, null, $"{agent.Hostname} in {agent.Site?.Name} is overdue.", AlertSeverity.Error, ct);
    public Task ResolveAvailabilityAsync(Agent agent, CancellationToken ct = default) => Resolve(agent, AlertType.Availability, null, null, ct);
}
