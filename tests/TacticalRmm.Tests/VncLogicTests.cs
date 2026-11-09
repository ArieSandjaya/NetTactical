using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

/// <summary>VNC password handling: DES auth/obfuscation, targets from agent data, encryption at rest.</summary>
public class VncLogicTests
{
    [Fact]
    public void ObfuscatedPasswordMatchesKnownTightVncValue()
    {
        // DES("password") with the standard VNC fixed key E84AD660C4721AE0; value cross-checked with OpenSSL.
        Assert.True(VncAuth.ObfuscateForFile("password") == "DBD83CFD727A1458", "known vector: " + VncAuth.ObfuscateForFile("password"));
        Assert.True(VncAuth.ObfuscateForFile("password1234") == VncAuth.ObfuscateForFile("password"), "only the first 8 characters count");
    }

    [Fact]
    public void ConnectionFileFormat()
    {
        var f = VncAuth.BuildConnectionFile("10.1.2.3", 5900, "password");
        Assert.True(f.Contains("host=10.1.2.3\r\n") && f.Contains("port=5900\r\n") && f.Contains("password=DBD83CFD727A1458\r\n"), f);
        Assert.True(f.StartsWith("[connection]"), "section header");
    }

    [Fact]
    public void ChallengeResponseIsDeterministicAndPasswordDependent()
    {
        var ch = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var a = VncAuth.Response("secret", ch);
        Assert.True(a.Length == 16, "16 bytes");
        Assert.True(a.SequenceEqual(VncAuth.Response("secret", ch)), "deterministic");
        Assert.True(!a.SequenceEqual(VncAuth.Response("secreT", ch)), "different password, different response");
    }

    [Fact]
    public void PasswordValidation()
    {
        Assert.True(VncAuth.ValidatePassword("12345678") is null, "8 chars ok");
        Assert.True(VncAuth.ValidatePassword("123456789") is not null, "9 chars rejected");
        Assert.True(VncAuth.ValidatePassword("") is not null && VncAuth.ValidatePassword(null) is not null, "empty rejected");
        Assert.True(VncAuth.ValidatePassword("pässwörd") is not null, "non-ascii rejected");
    }

    [Fact]
    public void LocalIpsFromWmiDetail()
    {
        var win = """{"network_config":[[{"Description":"Intel"},{"IPAddress":["192.168.1.20","fe80::1"]}],[{"IPAddress":null}],[{"IPAddress":["10.0.0.5"]},{"IPAddress":["172.16.0.9"]}]]}""";
        var ips = VncTargets.ParseLocalIps(win);
        Assert.True(ips.SequenceEqual(new[] { "192.168.1.20", "10.0.0.5" }), string.Join(",", ips));
        Assert.True(VncTargets.ParseLocalIps("""{"local_ips":["192.168.5.5","not-an-ip"]}""").SequenceEqual(new[] { "192.168.5.5" }), "linux shape");
        Assert.True(VncTargets.ParseLocalIps("{bad").Count == 0 && VncTargets.ParseLocalIps(null).Count == 0 && VncTargets.ParseLocalIps("[]").Count == 0, "garbage never throws");
    }

    [Fact]
    public void TargetsAreLanOnlyUnlessAllowed()
    {
        var wmi = """{"local_ips":["169.254.3.3","127.0.0.1","8.8.8.8","10.1.1.1","192.168.0.4","100.64.0.1"]}""";
        var lan = VncTargets.Candidates(wmi, "203.0.113.9", allowPublic: false);
        Assert.True(lan.SequenceEqual(new[] { "10.1.1.1", "192.168.0.4" }), "lan: " + string.Join(",", lan));
        var all = VncTargets.Candidates(wmi, "203.0.113.9", allowPublic: true);
        Assert.True(all.SequenceEqual(new[] { "10.1.1.1", "192.168.0.4", "8.8.8.8", "203.0.113.9" }), "public after private: " + string.Join(",", all));
        Assert.True(VncTargets.Candidates(null, "10.0.0.1", false).Count == 0, "public ip field is not used for lan-only");
        Assert.True(VncTargets.IsPrivate("172.16.5.1") && !VncTargets.IsPrivate("172.32.0.1") && !VncTargets.IsPrivate("11.0.0.1"), "rfc1918 ranges");
    }

    [Fact]
    public void SecretsRoundTripAndTamperDetection()
    {
        var secret = new string('k', 40);
        var p = VncSecrets.Protect("hunter2", secret);
        Assert.True(!p.Contains("hunter2"), "not plaintext");
        Assert.True(VncSecrets.Unprotect(p, secret) == "hunter2", "round trip");
        Assert.True(VncSecrets.Protect("hunter2", secret) != p, "random iv");
        Assert.True(VncSecrets.Unprotect(p, new string('x', 40)) is null, "wrong secret");
        var raw = Convert.FromBase64String(p); raw[^1] ^= 1;
        Assert.True(VncSecrets.Unprotect(Convert.ToBase64String(raw), secret) is null, "tampered");
        Assert.True(VncSecrets.Unprotect("%%%", secret) is null, "garbage");
    }
}
