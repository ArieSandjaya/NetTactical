using System.Net;
using System.Text.Json;
using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class NotificationRulesTests
{
    private static AlertEvent Ev(string kind = "opened", string sev = "error", bool? flag = null, string msg = "PC1 - Disk C: Failed") =>
        new(5, kind, "check", sev, msg, "PC1", "Head Office", "Main", "agent-1", new DateTime(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc), flag);

    [Fact]
    public void FilteringBySeverityResolveAndFlag()
    {
        var all = new ChannelSettings(true, "info", false, false);
        Assert.True(NotificationRules.ShouldSend(all, Ev(sev: "info")), "info passes info");
        Assert.True(!NotificationRules.ShouldSend(all, Ev("resolved")), "resolved off");
        Assert.True(NotificationRules.ShouldSend(all with { NotifyResolved = true }, Ev("resolved")), "resolved on");
        var crit = new ChannelSettings(true, "error", true, false);
        Assert.True(!NotificationRules.ShouldSend(crit, Ev(sev: "warning")), "warning below error");
        Assert.True(NotificationRules.ShouldSend(crit, Ev(sev: "error")), "error passes");
        Assert.True(!NotificationRules.ShouldSend(all with { Enabled = false }, Ev()), "disabled");
        var flagged = all with { OnlyFlagged = true };
        Assert.True(!NotificationRules.ShouldSend(flagged, Ev(flag: false)) && !NotificationRules.ShouldSend(flagged, Ev(flag: null)) && NotificationRules.ShouldSend(flagged, Ev(flag: true)), "only flagged");
    }

    [Fact]
    public void SubjectAndBodiesAreSingleLineSafe()
    {
        var e = Ev(msg: "bad\r\nBcc: evil@example.com");
        var s = NotificationRules.Subject(e);
        Assert.True(!s.Contains('\r') && !s.Contains('\n'), "no newline in subject");
        Assert.True(s.StartsWith("[NetTactical] CRITICAL: "), "severity word");
        Assert.True(NotificationRules.Subject(Ev("resolved")).StartsWith("[NetTactical] RESOLVED: "), "resolved word");
        Assert.True(NotificationRules.Subject(Ev(msg: new string('x', 900))).Length <= 200, "subject capped");
        Assert.True(NotificationRules.TextBody(Ev()).Contains("Head Office / Main"), "body has client/site");
    }

    [Fact]
    public void PayloadsAreValidJson()
    {
        using var j = JsonDocument.Parse(NotificationRules.JsonPayload(Ev()));
        Assert.True(j.RootElement.GetProperty("event").GetString() == "alert.opened" && j.RootElement.GetProperty("alert_id").GetInt32() == 5 && j.RootElement.GetProperty("agent_id").GetString() == "agent-1", "json fields");
        Assert.True(JsonDocument.Parse(NotificationRules.JsonPayload(Ev("resolved"))).RootElement.GetProperty("event").GetString() == "alert.resolved", "resolved event");
        Assert.True(JsonDocument.Parse(NotificationRules.SlackPayload(Ev())).RootElement.GetProperty("text").GetString()!.Contains("PC1"), "slack text");
    }

    [Fact]
    public void SignatureMatchesKnownHmac()
    {
        // RFC 4231 test case 2: key "Jefe", data "what do ya want for nothing?"
        Assert.True(NotificationRules.Sign("Jefe", "what do ya want for nothing?") == "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843", "hmac vector");
    }

    [Fact]
    public void WebhookUrlRules()
    {
        Assert.True(NotificationRules.ValidateWebhookUrl("https://hooks.slack.com/services/T/B/x") is null, "https ok");
        Assert.True(NotificationRules.ValidateWebhookUrl("http://10.0.0.20:8080/hook") is null, "LAN http ok");
        foreach (var bad in new[] { "", "ftp://a.com/x", "javascript:alert(1)", "http://localhost/x", "http://127.0.0.1/x", "http://[::1]/x", "http://169.254.169.254/latest/meta-data", "https://user:pw@a.com/x", "http://0.0.0.0/x", "http://[::ffff:127.0.0.1]/x", "not a url" })
            Assert.True(NotificationRules.ValidateWebhookUrl(bad) is not null, $"rejects '{bad}'");
        Assert.True(NotificationRules.IsBlockedAddress(IPAddress.Parse("169.254.10.1")) && !NotificationRules.IsBlockedAddress(IPAddress.Parse("192.168.1.5")), "address classes");
    }

    [Fact]
    public void EmailRules()
    {
        Assert.True(NotificationRules.SplitRecipients("a@x.com; b@x.com,\nA@X.COM").Count == 2, "split + dedupe");
        Assert.True(NotificationRules.ValidateEmail("smtp.example.com", 587, "rmm@example.com", ["ops@example.com"]) is null, "valid");
        Assert.True(NotificationRules.ValidateEmail("", 587, "rmm@example.com", ["ops@example.com"]) is not null, "host");
        Assert.True(NotificationRules.ValidateEmail("h", 0, "rmm@example.com", ["ops@example.com"]) is not null, "port");
        Assert.True(NotificationRules.ValidateEmail("h", 25, "nope", ["ops@example.com"]) is not null, "from");
        Assert.True(NotificationRules.ValidateEmail("h", 25, "a@b.com", []) is not null, "no recipients");
        Assert.True(NotificationRules.ValidateEmail("h", 25, "a@b.com", ["x@y.com\r\nBcc: z@y.com"]) is not null, "injection");
    }
}
