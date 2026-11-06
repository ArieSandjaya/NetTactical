using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Domain;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Jobs;

/// <summary>Pengganti celery beat "agent-outages-task" (setiap 150 detik).</summary>
public sealed class AgentOutagesJob(IServiceScopeFactory scopes, ILogger<AgentOutagesJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(150));
        do
        {
            try { await RunOnce(stop); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "agent outages job gagal"); }
        } while (await timer.WaitForNextTickAsync(stop));
    }

    internal async Task RunOnce(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();
        var now = DateTime.UtcNow;

        var agents = await db.Agents.Include(a => a.Site).Where(a => !a.MaintenanceMode && a.OverdueDashboardAlert).ToListAsync(ct);
        foreach (var a in agents)
        {
            var overdue = a.LastSeen is null || a.LastSeen < now.AddMinutes(-a.OverdueTime);
            if (overdue) await alerts.RaiseAvailabilityAsync(a, ct);
            else if (a.LastSeen >= now.AddMinutes(-a.OfflineTime)) await alerts.ResolveAvailabilityAsync(a, ct);
        }

        // Alert yang sudah di-snooze kembali aktif (celery "unsnooze-alerts")
        var snoozed = await db.Alerts.Where(x => x.Snoozed && x.SnoozeUntil != null && x.SnoozeUntil < now).ToListAsync(ct);
        foreach (var s in snoozed) { s.Snoozed = false; s.SnoozeUntil = null; }
        if (snoozed.Count > 0) await db.SaveChangesAsync(ct);
    }
}
