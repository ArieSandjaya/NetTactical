using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Jobs;

/// <summary>Every hour, checks the hardware of online Windows assets whose last check is older than Tactical:HardwareRefreshHours (0 = off).
/// At most 20 per round and one at a time, so a large fleet is spread over several hours instead of hitting NATS at once.</summary>
public sealed class HardwareRefreshJob(IServiceScopeFactory scopes, IOptions<TacticalOptions> opt, ILogger<HardwareRefreshJob> log) : BackgroundService
{
    private const int PerRound = 20;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (opt.Value.HardwareRefreshHours <= 0) return;
        try { await Task.Delay(TimeSpan.FromMinutes(5), stop); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await RoundAsync(stop); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "hardware refresh round failed"); }
        } while (await timer.WaitForNextTickAsync(stop));
    }

    private async Task RoundAsync(CancellationToken stop)
    {
        var now = DateTime.UtcNow;
        var stale = now.AddHours(-opt.Value.HardwareRefreshHours);
        List<string> ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seen = now.AddMinutes(-5);
            ids = await db.Agents.AsNoTracking()
                .Where(a => a.Plat == "windows" && a.LastSeen != null && a.LastSeen > seen)
                .Where(a => !db.HardwareInfos.Any(h => h.AgentId == a.Id && h.CollectedAt != null && h.CollectedAt > stale))
                .OrderBy(a => a.Id).Select(a => a.AgentId).Take(PerRound).ToListAsync(stop);
        }
        foreach (var id in ids)
        {
            await using var scope = scopes.CreateAsyncScope();
            var r = await scope.ServiceProvider.GetRequiredService<IHardwareService>().RefreshAsync(id, stop);
            if (!r.IsOk) log.LogDebug("hardware check of {Agent} skipped: {Error}", id, r.Error);
        }
        if (ids.Count > 0) log.LogInformation("hardware refresh: {Count} asset(s) checked", ids.Count);
    }
}
