using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>Dashboard alerts. A newly opened or resolved alert is also handed to the notification queue (email / webhook channels).</summary>
public sealed class AlertService(AppDbContext db, INotificationQueue notifications) : IAlertService
{
    private async Task<Alert> OpenOrCreate(Agent agent, string type, int? checkId, int? taskId, string msg, string severity, bool emailFlag, CancellationToken ct)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.AgentId == agent.Id && a.AlertType == type && !a.Resolved
            && a.AssignedCheckId == checkId && a.AssignedTaskId == taskId, ct);
        if (alert is not null) { alert.Severity = severity; await db.SaveChangesAsync(ct); return alert; }
        alert = new Alert { AgentId = agent.Id, AlertType = type, AssignedCheckId = checkId, AssignedTaskId = taskId,
            Message = msg, Severity = severity, AlertTime = DateTime.UtcNow };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(NotificationRules.Opened, alert, agent, emailFlag, ct);
        return alert;
    }

    private async Task Resolve(Agent agent, string type, int? checkId, int? taskId, bool emailFlag, CancellationToken ct)
    {
        var open = await db.Alerts.Where(a => a.AgentId == agent.Id && a.AlertType == type && !a.Resolved
            && a.AssignedCheckId == checkId && a.AssignedTaskId == taskId).ToListAsync(ct);
        foreach (var a in open) { a.Resolved = true; a.ResolvedOn = DateTime.UtcNow; }
        if (open.Count == 0) return;
        await db.SaveChangesAsync(ct);
        foreach (var a in open) await NotifyAsync(NotificationRules.Resolved, a, agent, emailFlag, ct);
    }

    /// <summary>Best effort: a problem while preparing a notification must never break check-in processing.</summary>
    private async Task NotifyAsync(string kind, Alert alert, Agent agent, bool emailFlag, CancellationToken ct)
    {
        try
        {
            var where = await db.Sites.AsNoTracking().Where(s => s.Id == agent.SiteId).Select(s => new { Site = s.Name, Client = s.Client.Name }).FirstOrDefaultAsync(ct);
            notifications.Enqueue(new AlertEvent(alert.Id, kind, alert.AlertType, alert.Severity, alert.Message ?? "", agent.Hostname, where?.Client ?? "", where?.Site ?? "",
                agent.AgentId, kind == NotificationRules.Resolved ? alert.ResolvedOn ?? DateTime.UtcNow : alert.AlertTime, emailFlag));
        }
        catch (Exception e) when (e is not OperationCanceledException) { /* the alert itself is already saved */ }
    }

    public Task RaiseCheckFailureAsync(Agent agent, Check check, CheckResult r, CancellationToken ct = default) =>
        OpenOrCreate(agent, AlertType.Check, check.Id, null, $"{agent.Hostname} - {check.Name ?? check.CheckType} Failed", r.AlertSeverity ?? check.AlertSeverity ?? AlertSeverity.Warning, check.EmailAlert, ct);
    public Task ResolveCheckAsync(Agent agent, Check check, CancellationToken ct = default) => Resolve(agent, AlertType.Check, check.Id, null, check.EmailAlert, ct);

    public Task RaiseTaskFailureAsync(Agent agent, AutomatedTask task, TaskResult r, CancellationToken ct = default) =>
        OpenOrCreate(agent, AlertType.Task, null, task.Id, $"{agent.Hostname} - {task.Name} Failed", task.AlertSeverity, task.EmailAlert, ct);
    public Task ResolveTaskAsync(Agent agent, AutomatedTask task, CancellationToken ct = default) => Resolve(agent, AlertType.Task, null, task.Id, task.EmailAlert, ct);

    public Task RaiseAvailabilityAsync(Agent agent, CancellationToken ct = default) =>
        OpenOrCreate(agent, AlertType.Availability, null, null, $"{agent.Hostname} in {agent.Site?.Name} is overdue.", AlertSeverity.Error, agent.OverdueEmailAlert, ct);
    public Task ResolveAvailabilityAsync(Agent agent, CancellationToken ct = default) => Resolve(agent, AlertType.Availability, null, null, agent.OverdueEmailAlert, ct);
}
