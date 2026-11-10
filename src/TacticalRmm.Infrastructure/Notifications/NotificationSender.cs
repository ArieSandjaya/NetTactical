using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Infrastructure.Notifications;

/// <summary>Delivers one alert event to one channel. Throws on failure; the caller records the message.</summary>
public static class NotificationSender
{
    // Webhooks never follow redirects, and every connection re-checks the resolved address so a hostname cannot be pointed at localhost or cloud metadata.
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5), ConnectCallback = ConnectAsync,
    }) { Timeout = TimeSpan.FromSeconds(10) };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var all = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
        var allowed = all.Where(a => !NotificationRules.IsBlockedAddress(a)).ToArray();
        if (allowed.Length == 0) throw new HttpRequestException("The webhook address is not allowed (loopback or link-local)");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try { await socket.ConnectAsync(allowed, ctx.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
        catch { socket.Dispose(); throw; }
    }

    public static Task SendAsync(NotificationChannel ch, string? secret, AlertEvent e, CancellationToken ct) =>
        ch.Kind == NotificationRules.Webhook ? SendWebhookAsync(ch, secret, e, ct) : SendEmailAsync(ch, secret, e, ct);

    private static async Task SendWebhookAsync(NotificationChannel ch, string? secret, AlertEvent e, CancellationToken ct)
    {
        if (NotificationRules.ValidateWebhookUrl(ch.Url) is { } bad) throw new InvalidOperationException(bad);
        var body = ch.Format == "slack" ? NotificationRules.SlackPayload(e) : NotificationRules.JsonPayload(e);
        using var req = new HttpRequestMessage(HttpMethod.Post, ch.Url!.Trim()) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.UserAgent.ParseAdd("NetTactical/1.0");
        if (!string.IsNullOrEmpty(secret)) req.Headers.Add("X-NetTactical-Signature", "sha256=" + NotificationRules.Sign(secret, body));
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"The webhook answered HTTP {(int)resp.StatusCode}");
    }

    private static async Task SendEmailAsync(NotificationChannel ch, string? secret, AlertEvent e, CancellationToken ct)
    {
        var to = NotificationRules.SplitRecipients(ch.Recipients);
        if (NotificationRules.ValidateEmail(ch.Host, ch.Port, ch.FromAddress, to) is { } bad) throw new InvalidOperationException(bad);
        using var msg = new MailMessage { From = new MailAddress(ch.FromAddress!.Trim()), Subject = NotificationRules.Subject(e), Body = NotificationRules.TextBody(e), IsBodyHtml = false };
        foreach (var a in to) msg.To.Add(a.Trim());
        // System.Net.Mail supports plain SMTP and STARTTLS (usually port 587); implicit TLS on port 465 is not supported.
        using var smtp = new SmtpClient(ch.Host!.Trim(), ch.Port) { EnableSsl = ch.StartTls, Timeout = 15000, DeliveryMethod = SmtpDeliveryMethod.Network };
        if (!string.IsNullOrWhiteSpace(ch.Username)) smtp.Credentials = new NetworkCredential(ch.Username, secret ?? "");
        await smtp.SendMailAsync(msg, ct);
    }
}
