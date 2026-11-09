using System.Net;
using System.Net.Sockets;
using System.Text;
using TacticalRmm.Application.Logic;
using TacticalRmm.Infrastructure.Remote;
using Xunit;

namespace TacticalRmm.Tests;

/// <summary>Runs the RFB authentication handshake against a small in-process fake VNC server.</summary>
public class VncProbeTests
{
    private static readonly TimeSpan T = TimeSpan.FromSeconds(3);

    /// <summary>mode: ok38 | ok33 | refuse (types=0 + reason) | notype2 | silent. Accepts one connection.</summary>
    private static (int Port, Task Done) Fake(string mode, string password, string reason = "Too many authentication failures")
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var task = Task.Run(async () =>
        {
            using var c = await l.AcceptTcpClientAsync(); l.Stop();
            var s = c.GetStream();
            async Task<byte[]> Rd(int n) { var b = new byte[n]; var o = 0; while (o < n) { var r = await s.ReadAsync(b.AsMemory(o, n - o)); if (r == 0) throw new EndOfStreamException(); o += r; } return b; }
            if (mode == "silent") { await Task.Delay(5000); return; }
            await s.WriteAsync(Encoding.ASCII.GetBytes(mode == "ok33" ? "RFB 003.003\n" : "RFB 003.008\n"));
            await Rd(12);
            if (mode == "refuse")
            {
                var r = Encoding.UTF8.GetBytes(reason);
                await s.WriteAsync(new byte[] { 0, 0, 0, (byte)(r.Length >> 8), (byte)r.Length });   // 0 security types, then u32 reason length
                await s.WriteAsync(r);
                return;
            }
            if (mode == "notype2") { await s.WriteAsync(new byte[] { 1, 1 }); return; }
            if (mode == "ok33") await s.WriteAsync(new byte[] { 0, 0, 0, 2 });
            else { await s.WriteAsync(new byte[] { 1, 2 }); await Rd(1); }
            var ch = Enumerable.Range(0, 16).Select(i => (byte)(i * 7 + 3)).ToArray();
            await s.WriteAsync(ch);
            var resp = await Rd(16);
            await s.WriteAsync(resp.SequenceEqual(VncAuth.Response(password, ch)) ? new byte[] { 0, 0, 0, 0 } : new byte[] { 0, 0, 0, 1 });
        });
        return (((IPEndPoint)l.LocalEndpoint).Port, task);
    }

    private static VncProbeStatus Run(string mode, string serverPw, string tryPw, string reason = "x")
    {
        var (port, done) = Fake(mode, serverPw, reason);
        var st = VncProbe.TryAsync("127.0.0.1", port, tryPw, T).GetAwaiter().GetResult();
        try { done.Wait(1000); } catch { }
        return st;
    }

    [Fact]
    public void ProbeAcceptsCorrectAndRejectsWrongPassword()
    {
        Assert.True(Run("ok38", "abc12345", "abc12345") == VncProbeStatus.Ok, "ok 3.8");
        Assert.True(Run("ok38", "abc12345", "wrong") == VncProbeStatus.BadPassword, "bad password 3.8");
        Assert.True(Run("ok33", "abc12345", "abc12345") == VncProbeStatus.Ok, "ok 3.3");
        Assert.True(Run("ok33", "abc12345", "nope") == VncProbeStatus.BadPassword, "bad password 3.3");
        Assert.True(Run("ok38", "longpassword", "longpass") == VncProbeStatus.Ok, "only the first 8 characters count");
    }

    [Fact]
    public void ProbeReportsRefusalsAndUnsupportedServers()
    {
        Assert.True(Run("refuse", "x", "x", "Too many authentication failures") == VncProbeStatus.TooManyAttempts, "too many attempts");
        Assert.True(Run("refuse", "x", "x", "Server is busy") == VncProbeStatus.ProtocolError, "other refusal");
        Assert.True(Run("notype2", "x", "x") == VncProbeStatus.Unsupported, "no vnc auth offered");
    }

    [Fact]
    public void ProbeReportsUnreachableAndTimeouts()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();   // nothing listens here
        Assert.True(VncProbe.TryAsync("127.0.0.1", port, "x", T).GetAwaiter().GetResult() == VncProbeStatus.Unreachable, "closed port");
        var (p2, done) = Fake("silent", "x");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var st = VncProbe.TryAsync("127.0.0.1", p2, "x", TimeSpan.FromMilliseconds(600)).GetAwaiter().GetResult();
        Assert.True(st == VncProbeStatus.Unreachable && sw.ElapsedMilliseconds < 3000, "silent server times out: " + st + " " + sw.ElapsedMilliseconds);
    }
}
