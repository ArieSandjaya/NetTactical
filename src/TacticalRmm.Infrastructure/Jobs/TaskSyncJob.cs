using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TacticalRmm.Application.Abstractions;

namespace TacticalRmm.Infrastructure.Jobs;

/// <summary>Sinkronisasi berkala task terjadwal ke agent (setara celery "sync-scheduled-tasks").</summary>
public sealed class TaskSyncJob(IServiceScopeFactory scopes, ILogger<TaskSyncJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var n = await scope.ServiceProvider.GetRequiredService<ITaskSyncService>().SyncPendingAsync(stop);
                if (n > 0) log.LogInformation("task sync: {Count} item diproses", n);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "task sync job gagal"); }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
