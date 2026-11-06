using System.Security.Cryptography;
using System.Text;

namespace TacticalRmm.Application.Logic;

/// <summary>Port meshctrl.utils.get_auth_token: AES-256-GCM, format iv(12)+tag(16)+cipher, base64 dengan '@' '$'.</summary>
public static class MeshAuth
{
    public static string CreateAuthToken(string user, string hexKey, long? unixTime = null, string domain = "")
    {
        var key = Convert.FromHexString(hexKey)[..32];
        var userId = user.Contains("user/") ? user.ToLowerInvariant() : $"user/{domain}/{user.ToLowerInvariant()}";
        var plain = Encoding.UTF8.GetBytes($"{{\"userid\":\"{userId}\", \"domainid\":\"{domain}\", \"time\":{unixTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}");

        var iv = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(iv, plain, cipher, tag);

        return Convert.ToBase64String([.. iv, .. tag, .. cipher]).Replace('+', '@').Replace('/', '$');
    }

    /// <summary>Hanya untuk uji: membalik token.</summary>
    public static string Decrypt(string token, string hexKey)
    {
        var raw = Convert.FromBase64String(token.Replace('@', '+').Replace('$', '/'));
        var key = Convert.FromHexString(hexKey)[..32];
        var plain = new byte[raw.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(raw[..12], raw[28..], raw[12..28], plain);
        return Encoding.UTF8.GetString(plain);
    }
}
