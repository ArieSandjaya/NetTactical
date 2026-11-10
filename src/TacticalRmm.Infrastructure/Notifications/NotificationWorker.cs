using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Notifications;

/// <summary>Reads alert events from the queue and delivers them to every matching channel, one at a time.</summary>
public sealed class NotificationWorker(NotificationQueue queue, IServiceScopeFactory scopes, IOptions<TacticalOptions> opt, ILogger<NotificationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await foreach (var e in queue.ReadAllAsync(stop))
        {
            try { await DeliverAsync(e, stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "notification delivery failed for alert {Id}", e.AlertId); }
        }
    }

    internal async Task DeliverAsync(AlertEvent e, CancellationToken stop)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channels = await db.NotificationChannels.Where(c => c.Enabled).ToListAsync(stop);
        foreach (var ch in channels)
        {
            if (!NotificationRules.ShouldSend(new ChannelSettings(ch.Enabled, ch.MinSeverity, ch.NotifyResolved, ch.OnlyFlagged), e)) continue;
            await RecordAsync(db, ch, async ct => await NotificationSender.SendAsync(ch, ch.Secret is null ? null : VncSecrets.Unprotect(ch.Secret, opt.Value.SecretKey), e, ct), stop);
        }
    }

    /// <summary>Runs one delivery with a time limit and stores the outcome on the channel (also used by the "send test" button).</summary>
    internal static async Task<NotificationTestResult> RecordAsync(AppDbContext db, NotificationChannel ch, Func<CancellationToken, Task> send, CancellationToken stop)
    {
        string? error = null;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop); limit.CancelAfter(TimeSpan.FromSeconds(25));
        try { await send(limit.Token); }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { error = "Timed out"; }
        catch (Exception ex) when (ex is not OperationCanceledException) { error = Short(ex); }
        ch.LastAttempt = DateTime.UtcNow; ch.LastOk = error is null; ch.LastError = error;
        await db.SaveChangesAsync(stop);
        return new NotificationTestResult(error is null, error);
    }

    private static string Short(Exception ex)
    {
        var m = (ex.InnerException?.Message is { Length: > 0 } inner && ex.Message.StartsWith("Failure sending mail") ? inner : ex.Message).Replace('\r', ' ').Replace('\n', ' ');
        return m.Length <= 300 ? m : m[..300];
    }
}
