using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using TacticalRmm.Application.Abstractions;

namespace TacticalRmm.Infrastructure.Messaging;

/// <summary>
/// Equivalent of agent.nats_cmd() in Django: subject = agent_id, payload msgpack.
/// The Go agent does not need any changes.
/// </summary>
public sealed class NatsCommandBus(INatsConnection nc, ILogger<NatsCommandBus> log) : IAgentCommandBus
{
    // Outgoing payloads use the plain options; everything received goes through AgentReplyDecoder (untrusted input).
    private static readonly MessagePackSerializerOptions Opts = ContractlessStandardResolver.Options;

    public async Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default)
    {
        var payload = MessagePackSerializer.Serialize(data, Opts);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var reply = await nc.RequestAsync<byte[], byte[]>(agentId, payload, cancellationToken: cts.Token);
            return AgentReplyDecoder.Decode(reply.Data);
        }
        catch (NatsNoRespondersException) { return "timeout"; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return "timeout"; }
        catch (NatsException e)
        {
            log.LogWarning(e, "NATS error during request to {Agent}", agentId);
            return "natsdown";
        }
    }

    public async Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default)
    {
        try { await nc.PublishAsync(agentId, MessagePackSerializer.Serialize(data, Opts), cancellationToken: ct); }
        catch (NatsException e) { log.LogWarning(e, "NATS publish to {Agent} failed", agentId); }
    }
}
