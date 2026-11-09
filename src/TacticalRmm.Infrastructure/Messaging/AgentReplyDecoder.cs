using MessagePack;
using MessagePack.Resolvers;

namespace TacticalRmm.Infrastructure.Messaging;

/// <summary>
/// Decodes MessagePack that comes from an endpoint. Treat it as untrusted: a compromised or spoofed endpoint (or anyone on an unencrypted
/// NATS link) can craft payloads that amplify memory use or recurse deeply (see the MessagePack-CSharp advisories fixed in 2.5.301/2.5.303).
/// Defence in depth on top of the patched package: size cap, hash-collision-resistant comparers, and a small object-graph depth limit.
/// </summary>
public static class AgentReplyDecoder
{
    /// <summary>Largest reply accepted (NATS itself allows 64 MB; real replies such as software lists are far smaller).</summary>
    public const int MaxReplyBytes = 32 * 1024 * 1024;
    /// <summary>Agent replies are shallow maps/arrays; the library default of 500 is far more than needed.</summary>
    public const int MaxDepth = 32;

    public static readonly MessagePackSerializerOptions Options =
        ContractlessStandardResolver.Options.WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(MaxDepth));

    /// <summary>Returns the decoded value, or the string "malformed" (the same style as "timeout"/"natsdown") when the payload is too large or invalid.</summary>
    public static object? Decode(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;
        if (data.Length > MaxReplyBytes) return "malformed";
        try { return MessagePackSerializer.Deserialize<object>(data, Options); }
        catch (MessagePackSerializationException) { return "malformed"; }
    }
}
