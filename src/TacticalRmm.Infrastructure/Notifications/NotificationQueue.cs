using System.Threading.Channels;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Logic;

namespace TacticalRmm.Infrastructure.Notifications;

/// <summary>In-memory queue between alert creation and delivery. Bounded: when a flood of alerts outpaces the senders the oldest events are dropped rather than slowing agents down.</summary>
public sealed class NotificationQueue : INotificationQueue
{
    private readonly Channel<AlertEvent> channel = Channel.CreateBounded<AlertEvent>(new BoundedChannelOptions(1000)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Enqueue(AlertEvent e) => channel.Writer.TryWrite(e);
    public IAsyncEnumerable<AlertEvent> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
}
