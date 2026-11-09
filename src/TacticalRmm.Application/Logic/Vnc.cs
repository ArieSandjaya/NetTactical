using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TacticalRmm.Application.Logic;

/// <summary>RFB "VNC authentication" (security type 2) and the obfuscated password format used in .vnc files. Pure logic, no I/O.</summary>
public static class VncAuth
{
    /// <summary>VNC passwords are limited to 8 ASCII characters (anything longer is silently ignored by the server).</summary>
    public const int MaxPasswordLength = 8;

    // VNC uses DES with each key byte bit-reversed; this is the fixed key used to obfuscate passwords stored in .vnc files / the registry.
    private static readonly byte[] FixedKey = [23, 82, 107, 6, 35, 78, 88, 7];

    public static string? ValidatePassword(string? p) =>
        string.IsNullOrEmpty(p) ? "password is required"
        : p.Length > MaxPasswordLength ? $"VNC passwords are limited to {MaxPasswordLength} characters (the server ignores the rest)"
        : p.Any(c => c < 0x20 || c > 0x7E) ? "password may only contain printable ASCII characters"
        : null;

    private static byte[] Reverse(byte[] key) => key.Select(b =>
    {
        byte r = 0;
        for (var i = 0; i < 8; i++) if ((b & (1 << i)) != 0) r |= (byte)(0x80 >> i);
        return r;
    }).ToArray();

    private static byte[] Des(byte[] vncKey, byte[] data)
    {
        using var des = DES.Create();
        des.Mode = CipherMode.ECB; des.Padding = PaddingMode.None;
        des.Key = Reverse(vncKey);
        using var enc = des.CreateEncryptor();
        return enc.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] KeyFrom(string password)
    {
        var key = new byte[8];
        Encoding.ASCII.GetBytes(password.Length > 8 ? password[..8] : password).CopyTo(key, 0);
        return key;
    }

    /// <summary>The 16-byte response to the server's 16-byte challenge. Throws CryptographicException for DES weak keys.</summary>
    public static byte[] Response(string password, byte[] challenge)
    {
        if (challenge.Length != 16) throw new ArgumentException("challenge must be 16 bytes");
        return Des(KeyFrom(password), challenge);
    }

    /// <summary>Hex string stored as "password=" in a .vnc connection file (TightVNC/RealVNC format).</summary>
    public static string ObfuscateForFile(string password) => Convert.ToHexString(Des(FixedKey, KeyFrom(password)));

    public static string BuildConnectionFile(string host, int port, string password) =>
        $"[connection]\r\nhost={host}\r\nport={port}\r\npassword={ObfuscateForFile(password)}\r\n";
}

public enum VncTargetKind { Private, Public }

/// <summary>Where to reach an agent: local IPs come from the agent's wmi_detail (written by nats-api), plus its public IP.</summary>
public static class VncTargets
{
    /// <summary>IPv4 addresses from wmi_detail (Windows: network_config -> [[{IPAddress:[..]}]]; Linux/mac: local_ips -> [..]). Never throws.</summary>
    public static IReadOnlyList<string> ParseLocalIps(string? wmiDetailJson)
    {
        var ips = new List<string>();
        if (string.IsNullOrWhiteSpace(wmiDetailJson)) return ips;
        try
        {
            using var doc = JsonDocument.Parse(wmiDetailJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return ips;
            if (doc.RootElement.TryGetProperty("local_ips", out var li) && li.ValueKind == JsonValueKind.Array)
                foreach (var e in li.EnumerateArray()) Add(ips, e);
            if (doc.RootElement.TryGetProperty("network_config", out var nc) && nc.ValueKind == JsonValueKind.Array)
                foreach (var adapter in nc.EnumerateArray())
                {
                    // Each adapter is a list of single-key dicts; take the first IPAddress entry, like the Django implementation.
                    var objs = adapter.ValueKind == JsonValueKind.Array ? adapter.EnumerateArray() : Enumerable.Repeat(adapter, 1);
                    foreach (var o in objs)
                    {
                        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("IPAddress", out var addr)) continue;
                        if (addr.ValueKind == JsonValueKind.Array) foreach (var a in addr.EnumerateArray()) Add(ips, a);
                        else Add(ips, addr);
                        break;
                    }
                }
        }
        catch (JsonException) { }
        return ips.Distinct().ToList();
    }

    private static void Add(List<string> ips, JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String && IsIPv4(e.GetString()) is { } ip) ips.Add(ip);
    }

    private static string? IsIPv4(string? s) =>
        s is not null && IPAddress.TryParse(s.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetwork && s.Trim().Count(c => c == '.') == 3 ? ip.ToString() : null;

    public static bool IsPrivate(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = a.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>Loopback, link-local, multicast/reserved, unspecified, and CGNAT are never valid VNC targets.</summary>
    public static bool IsNeverAllowed(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork) return true;
        var b = a.GetAddressBytes();
        return b[0] == 0 || b[0] == 127 || b[0] >= 224 || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    /// <summary>Candidate hosts in order: private LAN addresses first; public IP only when <paramref name="allowPublic"/> is set.
    /// The target is always taken from the agent's own record, never from request input.</summary>
    public static IReadOnlyList<string> Candidates(string? wmiDetailJson, string? publicIp, bool allowPublic)
    {
        var all = ParseLocalIps(wmiDetailJson).ToList();
        if (allowPublic && IsIPv4(publicIp) is { } p) all.Add(p);
        return all.Where(ip => !IsNeverAllowed(ip) && (IsPrivate(ip) || allowPublic)).Distinct()
            .OrderBy(ip => IsPrivate(ip) ? 0 : 1).Take(4).ToList();
    }
}

/// <summary>Encrypts stored VNC passwords (AES-256-GCM, key derived from the server secret). Format: base64(iv12 + tag16 + cipher).</summary>
public static class VncSecrets
{
    private static byte[] Key(string secret) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32, salt: null, info: Encoding.ASCII.GetBytes("nettactical-vnc-credential-v1"));

    public static string Protect(string plain, string secret)
    {
        var iv = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Key(secret), 16)) aes.Encrypt(iv, data, cipher, tag);
        return Convert.ToBase64String([.. iv, .. tag, .. cipher]);
    }

    /// <summary>Returns null if the value is corrupt or the server secret changed.</summary>
    public static string? Unprotect(string protectedValue, string secret)
    {
        try
        {
            var raw = Convert.FromBase64String(protectedValue);
            if (raw.Length < 28) return null;
            var plain = new byte[raw.Length - 28];
            using var aes = new AesGcm(Key(secret), 16);
            aes.Decrypt(raw[..12], raw[28..], raw[12..28], plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException) { return null; }
    }
}
