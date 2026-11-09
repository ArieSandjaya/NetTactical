using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using TacticalRmm.Application.Logic;

namespace TacticalRmm.Infrastructure.Remote;

public enum VncProbeStatus { Ok, Unreachable, BadPassword, TooManyAttempts, Unsupported, ProtocolError }

/// <summary>Checks that a VNC server is reachable and accepts a password by completing the RFB authentication handshake only (no session is opened).</summary>
public static class VncProbe
{
    public static async Task<VncProbeStatus> TryAsync(string host, int port, string password, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var tcp = new TcpClient { NoDelay = true };
            try { await tcp.ConnectAsync(host, port, cts.Token); }
            catch (SocketException) { return VncProbeStatus.Unreachable; }
            var s = tcp.GetStream();

            var ver = Encoding.ASCII.GetString(await ReadAsync(s, 12, cts.Token));
            if (!ver.StartsWith("RFB ", StringComparison.Ordinal) || !int.TryParse(ver.AsSpan(8, 3), out var minor)) return VncProbeStatus.ProtocolError;
            var v38 = minor >= 7;
            await s.WriteAsync(Encoding.ASCII.GetBytes(v38 ? "RFB 003.008\n" : "RFB 003.003\n"), cts.Token);

            if (v38)
            {
                var n = (await ReadAsync(s, 1, cts.Token))[0];
                if (n == 0) return await ReasonAsync(s, cts.Token);
                var types = await ReadAsync(s, n, cts.Token);
                if (!types.Contains((byte)2)) return VncProbeStatus.Unsupported;
                await s.WriteAsync(new byte[] { 2 }, cts.Token);
            }
            else
            {
                var t = await ReadAsync(s, 4, cts.Token);
                if (t[3] == 0) return await ReasonAsync(s, cts.Token);   // 3.3: type 0 = connection failed, reason follows
                if (t[3] != 2) return VncProbeStatus.Unsupported;
            }

            var challenge = await ReadAsync(s, 16, cts.Token);
            byte[] response;
            try { response = VncAuth.Response(password, challenge); }
            catch (CryptographicException) { return VncProbeStatus.BadPassword; }   // DES weak key: this password cannot be used with VNC auth
            await s.WriteAsync(response, cts.Token);

            var res = await ReadAsync(s, 4, cts.Token);
            return res[3] switch { 0 => VncProbeStatus.Ok, 1 => VncProbeStatus.BadPassword, 2 => VncProbeStatus.TooManyAttempts, _ => VncProbeStatus.ProtocolError };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return VncProbeStatus.Unreachable; }
        catch (EndOfStreamException) { return VncProbeStatus.ProtocolError; }
        catch (IOException) { return VncProbeStatus.ProtocolError; }
    }

    /// <summary>A server that refuses with a reason string (e.g. TightVNC "Too many authentication failures").</summary>
    private static async Task<VncProbeStatus> ReasonAsync(NetworkStream s, CancellationToken ct)
    {
        try
        {
            var len = await ReadAsync(s, 4, ct);
            var n = (len[0] << 24) | (len[1] << 16) | (len[2] << 8) | len[3];
            if (n is < 0 or > 1024) return VncProbeStatus.ProtocolError;
            var reason = Encoding.UTF8.GetString(await ReadAsync(s, n, ct));
            return reason.Contains("many", StringComparison.OrdinalIgnoreCase) || reason.Contains("attempt", StringComparison.OrdinalIgnoreCase)
                ? VncProbeStatus.TooManyAttempts : VncProbeStatus.ProtocolError;
        }
        catch (Exception e) when (e is EndOfStreamException or IOException) { return VncProbeStatus.ProtocolError; }
    }

    private static async Task<byte[]> ReadAsync(NetworkStream s, int count, CancellationToken ct)
    {
        var buf = new byte[count];
        var off = 0;
        while (off < count)
        {
            var r = await s.ReadAsync(buf.AsMemory(off, count - off), ct);
            if (r == 0) throw new EndOfStreamException();
            off += r;
        }
        return buf;
    }
}
