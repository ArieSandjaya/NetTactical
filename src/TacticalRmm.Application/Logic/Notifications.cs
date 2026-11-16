using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TacticalRmm.Domain;

namespace TacticalRmm.Application.Logic;

/// <summary>What happened to an alert. <see cref="Kind"/> is "opened" or "resolved".</summary>
public record AlertEvent(int AlertId, string Kind, string AlertType, string Severity, string Message, string Hostname, string Client, string Site,
    string AgentPublicId, DateTime Time, bool? EmailFlag);

/// <summary>The parts of a channel that decide whether an event is delivered.</summary>
public record ChannelSettings(bool Enabled, string MinSeverity, bool NotifyResolved, bool OnlyFlagged);

/// <summary>Pure rules for alert notifications: filtering, message text, payloads, signing and input validation.</summary>
public static class NotificationRules
{
    public const string Email = "email", Webhook = "webhook", Opened = "opened", Resolved = "resolved";
    public static readonly IReadOnlyList<string> Kinds = [Email, Webhook];
    public static readonly IReadOnlyList<string> Formats = ["json", "slack"];
    public static readonly IReadOnlyList<string> Severities = [AlertSeverity.Info, AlertSeverity.Warning, AlertSeverity.Error];

    public static int SeverityRank(string? s) => s switch { AlertSeverity.Error => 2, AlertSeverity.Warning => 1, _ => 0 };

    public static bool ShouldSend(ChannelSettings c, AlertEvent e) =>
        c.Enabled
        && SeverityRank(e.Severity) >= SeverityRank(c.MinSeverity)
        && (e.Kind != Resolved || c.NotifyResolved)
        && (!c.OnlyFlagged || e.EmailFlag == true);

    private static string Clean(string? s, int max = 300)
    {
        var t = new string((s ?? "").Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
        return t.Length <= max ? t : t[..max];
    }

    private static string Word(AlertEvent e) => e.Kind == Resolved ? "RESOLVED" : e.Severity switch { AlertSeverity.Error => "CRITICAL", AlertSeverity.Warning => "WARNING", _ => "INFO" };

    /// <summary>Single line, no control characters (also prevents header injection).</summary>
    public static string Subject(AlertEvent e) => Clean($"[NetTactical] {Word(e)}: {e.Message}", 200);

    public static string TextBody(AlertEvent e) =>
        $"{Clean(e.Message, 500)}\n\nStatus:   {(e.Kind == Resolved ? "Resolved" : "Open")}\nSeverity: {e.Severity}\nType:     {e.AlertType}\n" +
        $"Device:   {Clean(e.Hostname)}\nClient:   {Clean(e.Client)} / {Clean(e.Site)}\nTime:     {e.Time:yyyy-MM-dd HH:mm:ss} UTC\n";

    /// <summary>Generic JSON body for custom receivers.</summary>
    public static string JsonPayload(AlertEvent e) => JsonSerializer.Serialize(new
    {
        @event = e.Kind == Resolved ? "alert.resolved" : "alert.opened", alert_id = e.AlertId, type = e.AlertType, severity = e.Severity, message = Clean(e.Message, 500),
        hostname = Clean(e.Hostname), client = Clean(e.Client), site = Clean(e.Site), agent_id = e.AgentPublicId, time = e.Time.ToUniversalTime().ToString("o"),
    });

    /// <summary>Body accepted by Slack, Mattermost, Teams (incoming webhook) and Discord-in-Slack-mode: {"text": "..."}.</summary>
    public static string SlackPayload(AlertEvent e) => JsonSerializer.Serialize(new { text = $"*{Subject(e)}*\n{Clean(e.Hostname)} · {Clean(e.Client)} / {Clean(e.Site)}" });

    /// <summary>Hex HMAC-SHA256 of the body, sent as X-NetTactical-Signature: sha256=&lt;hex&gt; so receivers can verify the sender.</summary>
    public static string Sign(string secret, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public static bool IsBlockedAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None)) return true;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.IsIPv6LinkLocal || ip.IsIPv6Multicast;
        var b = ip.GetAddressBytes();
        return (b[0] == 169 && b[1] == 254) || b[0] >= 224;   // link-local (cloud metadata) and multicast/reserved
    }

    /// <summary>null when valid. http(s) only, no credentials in the URL, never loopback or link-local targets.</summary>
    public static string? ValidateWebhookUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2000 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return "url must be a full http(s) address";
        if (u.Scheme is not ("http" or "https")) return "url must start with http:// or https://";
        if (!string.IsNullOrEmpty(u.UserInfo)) return "url must not contain a username or password";
        var host = u.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host == "localhost" || host.EndsWith(".localhost") || host.EndsWith(".internal")) return "url must not point to this server (localhost)";
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IsBlockedAddress(ip)) return "url must not point to a loopback or link-local address";
        return null;
    }

    public static bool IsValidAddress(string? a) => !string.IsNullOrWhiteSpace(a) && a.Length <= 254 && !a.Any(char.IsControl) && MailAddress.TryCreate(a.Trim(), out var m) && m.Address == a.Trim();

    /// <summary>Splits on commas, semicolons, spaces and new lines.</summary>
    public static List<string> SplitRecipients(string? raw) =>
        (raw ?? "").Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string? ValidateEmail(string? host, int port, string? from, IReadOnlyList<string> to)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return "SMTP host is required";
        if (port is < 1 or > 65535) return "SMTP port must be between 1 and 65535";
        if (!IsValidAddress(from)) return "From address is not a valid email address";
        if (to.Count == 0) return "Add at least one recipient";
        if (to.Count > 50) return "At most 50 recipients";
        var bad = to.FirstOrDefault(a => !IsValidAddress(a));
        return bad is null ? null : $"'{bad}' is not a valid email address";
    }
}
