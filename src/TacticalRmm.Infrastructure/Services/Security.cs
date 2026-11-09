using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000;
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var h = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(h)}";
    }

    public bool Verify(string password, string hash)
    {
        var p = hash.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2" || !int.TryParse(p[1], out var it)) return false;
        var expected = Convert.FromBase64String(p[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[2]), it, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

public sealed class TokenService(AppDbContext db, IOptions<TacticalOptions> opt) : ITokenService
{
    private TimeSpan Ttl => TimeSpan.FromHours(opt.Value.UserTokenTtlHours);
    private static readonly TimeSpan MinRefresh = TimeSpan.FromMinutes(10);

    private static string Digest(string raw) => Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    public async Task<(string RawToken, DateTime Expiry)> CreateUserTokenAsync(int userId, CancellationToken ct = default)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(); // 64 hex
        var expiry = DateTime.UtcNow + Ttl;
        db.UserTokens.Add(new UserToken { Digest = Digest(raw), TokenKey = raw[..8], UserId = userId, Created = DateTime.UtcNow, Expiry = expiry });
        await db.SaveChangesAsync(ct);
        return (raw, expiry);
    }

    public async Task<(string RawToken, DateTime Expiry)> CreateInstallerTokenAsync(int installerUserId, TimeSpan lifetime, CancellationToken ct = default)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiry = DateTime.UtcNow + lifetime;
        db.UserTokens.Add(new UserToken { Digest = Digest(raw), TokenKey = raw[..8], UserId = installerUserId, Created = DateTime.UtcNow, Expiry = expiry });
        await db.SaveChangesAsync(ct);
        return (raw, expiry);
    }

    public async Task<string> CreateAgentTokenAsync(int userId, CancellationToken ct = default)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant(); // 40 hex like the DRF authtoken
        db.AgentTokens.Add(new AgentToken { Key = key, UserId = userId, Created = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return key;
    }

    public async Task<User?> ValidateAsync(string raw, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 8) return null;

        var users = db.Users.Include(u => u.Role).ThenInclude(r => r!.CanViewClients)
                            .Include(u => u.Role).ThenInclude(r => r!.CanViewSites);

        var agentTok = await db.AgentTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Key == raw, ct);
        if (agentTok is not null)
            return await users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == agentTok.UserId && u.IsActive, ct);

        var key = raw[..8];
        var digest = Digest(raw);
        var candidates = await db.UserTokens.Where(t => t.TokenKey == key).ToListAsync(ct);
        foreach (var t in candidates)
        {
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(t.Digest), Encoding.ASCII.GetBytes(digest))) continue;
            if (t.Expiry is { } exp && exp < DateTime.UtcNow) { db.UserTokens.Remove(t); await db.SaveChangesAsync(ct); return null; }

            var owner = await users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == t.UserId && u.IsActive, ct);
            // AUTO_REFRESH for dashboard sessions only: an installer token keeps the lifetime it was issued with.
            if (owner is { IsInstallerUser: false } && t.Expiry is { } e2 && DateTime.UtcNow + Ttl - e2 > MinRefresh)
            { t.Expiry = DateTime.UtcNow + Ttl; await db.SaveChangesAsync(ct); }

            return owner;
        }
        return null;
    }

    public async Task RevokeAsync(string raw, CancellationToken ct = default)
    {
        var digest = Digest(raw);
        var t = await db.UserTokens.FirstOrDefaultAsync(x => x.Digest == digest, ct);
        if (t is null) return;
        db.UserTokens.Remove(t);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>TOTP RFC 6238 (HMAC-SHA1, 6 digit, 30 s) — compatible with pyotp / Google Authenticator.</summary>
public static class Totp
{
    public static bool Verify(string base32Secret, string code, int window = 1)
    {
        if (code.Length != 6) return false;
        var key = Base32Decode(base32Secret);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        for (var i = -window; i <= window; i++)
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(key, step + i)), Encoding.ASCII.GetBytes(code)))
                return true;
        return false;
    }

    public static string GenerateSecret()
    {
        const string alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = RandomNumberGenerator.GetBytes(20);
        var sb = new StringBuilder(); int buffer = 0, bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { bits -= 5; sb.Append(alpha[(buffer >> bits) & 31]); }
        }
        if (bits > 0) sb.Append(alpha[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static string ProvisioningUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}";

    private static string Compute(byte[] key, long counter)
    {
        var msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        var h = HMACSHA1.HashData(key, msg);
        var o = h[^1] & 0x0F;
        var bin = ((h[o] & 0x7F) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        return (bin % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string s)
    {
        const string alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>(); int buffer = 0, bits = 0;
        foreach (var c in s.TrimEnd('=').ToUpperInvariant())
        {
            var v = alpha.IndexOf(c);
            if (v < 0) continue;
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)((buffer >> bits) & 0xFF)); }
        }
        return bytes.ToArray();
    }
}
