using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>
/// Once a minute: for every agent that has approved updates waiting, check whether its effective schedule is due (in the agent's own time zone)
/// and, if the agent answers a ping, tell it to install them. If the agent is offline the attempt repeats every minute until the end of the scheduled hour.
/// </summary>
public sealed class WinUpdateScheduler(IServiceScopeFactory scopes, ILogger<WinUpdateScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try { await TickAsync(DateTime.UtcNow, stop); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("Windows Update scheduler tick failed: {Type}", e.GetType().Name); }
        }
    }

    public async Task TickAsync(DateTime utcNow, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = (WinUpdateAdminService)scope.ServiceProvider.GetRequiredService<IWinUpdateAdminService>();

        var ids = await db.WinUpdates.AsNoTracking().Where(u => u.Action == "approve" && !u.Installed).Select(u => u.AgentId).Distinct().ToListAsync(ct);
        foreach (var id in ids)
        {
            var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (agent is null) continue;
            var (own, parent) = await PatchPolicyLoader.LoadAsync(db, agent, ct);
            var eff = WinUpdateRules.Resolve(own, parent);
            var zone = WinUpdateRules.ZoneOf(agent.TimeZone);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
            var last = own?.LastRunAt is { } l ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(l, DateTimeKind.Utc), zone) : (DateTime?)null;
            if (!WinUpdateRules.IsDue(eff, local, last)) continue;

            var r = await svc.InstallApprovedAsync(agent, ct);
            if (!r.AgentOnline) continue;                       // try again next minute, still inside the window
            var row = await db.WinUpdatePolicies.FirstOrDefaultAsync(p => p.AgentId == id, ct);
            if (row is null) { row = new WinUpdatePolicy { AgentId = id }; db.WinUpdatePolicies.Add(row); }
            row.LastRunAt = utcNow;
            await db.SaveChangesAsync(ct);
        }
    }
}
