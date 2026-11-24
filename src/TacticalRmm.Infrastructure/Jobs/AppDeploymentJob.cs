using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Infrastructure.Services;

namespace TacticalRmm.Infrastructure.Jobs;

/// <summary>Every 30 seconds, installs waiting deployments on assets that are online: at most 8 at a time. Computers that are offline stay waiting
/// (up to the deployment's expiry) and are picked up as soon as they report in again.</summary>
public sealed class AppDeploymentJob(IServiceScopeFactory scopes, IAgentCommandBus bus, ILogger<AppDeploymentJob> log) : BackgroundService
{
    private const int Parallel = 8;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stop); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try { await RoundAsync(stop); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "app deployment round failed"); }
        } while (await timer.WaitForNextTickAsync(stop));
    }

    private async Task RoundAsync(CancellationToken stop)
    {
        IReadOnlyList<AppWork> work;
        await using (var scope = scopes.CreateAsyncScope())
            work = await scope.ServiceProvider.GetRequiredService<IAppDeploymentService>().ClaimDueAsync(Parallel, stop);
        if (work.Count == 0) return;
        await Task.WhenAll(work.Select(async w =>
        {
            string? payload = null, error;
            try { (payload, error) = await AgentScripts.RunAsync(bus, w.AgentId, w.Script, TimeSpan.FromSeconds(w.TimeoutSeconds), stop); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "deployment of {Package} to {Host} failed", w.PackageName, w.Hostname); error = "Unexpected error on the server."; }
            // Even if the server is stopping, the answer is written so the target does not stay on "running".
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAppDeploymentService>().FinishAsync(w, payload, error, CancellationToken.None);
        }));
        log.LogInformation("app deployment: {Count} install(s) finished", work.Count);
    }
}
