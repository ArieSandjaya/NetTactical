using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Jobs;

/// <summary>Hapus metrik historis yang lebih tua dari Tactical:MetricRetentionDays (cek tiap 6 jam).</summary>
public sealed class MetricRetentionJob(IServiceScopeFactory scopes, IOptions<TacticalOptions> opt, ILogger<MetricRetentionJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, opt.Value.MetricRetentionDays));
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var n = await db.CheckMetrics.Where(m => m.Time < cutoff).ExecuteDeleteAsync(stop);
                if (n > 0) log.LogInformation("metric retention: {Count} titik dihapus", n);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "metric retention gagal"); }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
