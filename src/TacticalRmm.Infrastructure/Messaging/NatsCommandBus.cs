using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using TacticalRmm.Application.Abstractions;

namespace TacticalRmm.Infrastructure.Messaging;

/// <summary>
/// Setara agent.nats_cmd() di Django: subject = agent_id, payload msgpack.
/// Agent Go tidak perlu diubah sama sekali.
/// </summary>
public sealed class NatsCommandBus(INatsConnection nc, ILogger<NatsCommandBus> log) : IAgentCommandBus
{
    private static readonly MessagePackSerializerOptions Opts = ContractlessStandardResolver.Options;

    public async Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default)
    {
        var payload = MessagePackSerializer.Serialize(data, Opts);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var reply = await nc.RequestAsync<byte[], byte[]>(agentId, payload, cancellationToken: cts.Token);
            return reply.Data is { Length: > 0 } d ? MessagePackSerializer.Deserialize<object>(d, Opts) : null;
        }
        catch (NatsNoRespondersException) { return "timeout"; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return "timeout"; }
        catch (NatsException e)
        {
            log.LogWarning(e, "NATS error saat request ke {Agent}", agentId);
            return "natsdown";
        }
    }

    public async Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default)
    {
        try { await nc.PublishAsync(agentId, MessagePackSerializer.Serialize(data, Opts), cancellationToken: ct); }
        catch (NatsException e) { log.LogWarning(e, "NATS publish gagal ke {Agent}", agentId); }
    }
}
