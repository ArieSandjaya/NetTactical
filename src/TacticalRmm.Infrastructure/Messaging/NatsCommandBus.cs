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

    // When the NATS server is down, a request would otherwise wait for its full timeout (up to a minute) and keep the browser request,
    // and with it the dashboard, hanging. A connection that is not open gets only a short window to come up.
    private static readonly TimeSpan ConnectGrace = TimeSpan.FromSeconds(3);
    private TimeSpan Budget(TimeSpan timeout) => nc.ConnectionState == NatsConnectionState.Open || timeout < ConnectGrace ? timeout : ConnectGrace;

    public async Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default)
    {
        var payload = MessagePackSerializer.Serialize(data, Opts);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Budget(timeout));
            var reply = await nc.RequestAsync<byte[], byte[]>(agentId, payload, cancellationToken: cts.Token);
            return AgentReplyDecoder.Decode(reply.Data);
        }
        catch (NatsNoRespondersException) { return "timeout"; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return nc.ConnectionState == NatsConnectionState.Open ? "timeout" : "natsdown"; }
        catch (NatsException e)
        {
            log.LogWarning("NATS error during request to {Agent}: {Message} (is the NATS server running?)", agentId, e.Message);
            return "natsdown";
        }
    }

    public async Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Budget(TimeSpan.FromSeconds(30)));
            await nc.PublishAsync(agentId, MessagePackSerializer.Serialize(data, Opts), cancellationToken: cts.Token);
        }
        catch (NatsException e) { log.LogWarning("NATS publish to {Agent} failed: {Message}", agentId, e.Message); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { log.LogWarning("NATS publish to {Agent} skipped: NATS server is not reachable", agentId); }
    }
}
