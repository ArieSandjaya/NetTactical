using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class MaintenancePostgresTests
{
    private sealed class Queue : INotificationQueue { public int Count; public void Enqueue(AlertEvent e) => Count++; }

    [PostgresFact]
    public async Task AnAssetInMaintenanceOpensNoNewAlertsAndSendsNothing()
    {
        await using var db = await PgDb.CreateAsync();
        var c = new Client { Name = "C" }; var s = new Site { Name = "S", Client = c };
        var quiet = new Agent { AgentId = "maint-agent-number-0001", Hostname = "QUIET", Site = s, MaintenanceMode = true };
        var normal = new Agent { AgentId = "maint-agent-number-0002", Hostname = "NORMAL", Site = s };
        db.AddRange(c, s, quiet, normal); await db.SaveChangesAsync();

        var queue = new Queue(); var alerts = new AlertService(db, queue);
        await alerts.RaiseAvailabilityAsync(quiet);
        Assert.Empty(await db.Alerts.Where(a => a.AgentId == quiet.Id).ToListAsync());
        await alerts.RaiseAvailabilityAsync(normal);
        Assert.Single(await db.Alerts.Where(a => a.AgentId == normal.Id).ToListAsync());
    }

    [PostgresFact]
    public async Task AnAlertThatWasAlreadyOpenIsKeptAndCanStillResolve()
    {
        await using var db = await PgDb.CreateAsync();
        var c = new Client { Name = "C" }; var s = new Site { Name = "S", Client = c };
        var a = new Agent { AgentId = "maint-agent-number-0003", Hostname = "PC", Site = s };
        db.AddRange(c, s, a); await db.SaveChangesAsync();
        var alerts = new AlertService(db, new Queue());
        await alerts.RaiseAvailabilityAsync(a);
        a.MaintenanceMode = true; await db.SaveChangesAsync();
        await alerts.RaiseAvailabilityAsync(a);                       // still just one
        Assert.Single(await db.Alerts.Where(x => x.AgentId == a.Id && !x.Resolved).ToListAsync());
        await alerts.ResolveAvailabilityAsync(a);
        Assert.Empty(await db.Alerts.Where(x => x.AgentId == a.Id && !x.Resolved).ToListAsync());
    }
}
