using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Notifications;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class NotificationAdminService(AppDbContext db, IAuditService audit, IOptions<TacticalOptions> opt) : INotificationAdminService
{
    private string Key => opt.Value.SecretKey;

    private static NotificationChannelDto ToDto(NotificationChannel c) => new(c.Id, c.Name, c.Kind, c.Enabled, c.MinSeverity, c.NotifyResolved, c.OnlyFlagged,
        c.Host, c.Port, c.StartTls, c.Username, c.FromAddress, c.Recipients, c.Url, c.Format, !string.IsNullOrEmpty(c.Secret), c.LastAttempt, c.LastOk, c.LastError);

    public async Task<Outcome<IReadOnlyList<NotificationChannelDto>>> ListAsync(CancellationToken ct = default) =>
        Outcome.Ok<IReadOnlyList<NotificationChannelDto>>((await db.NotificationChannels.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(ToDto).ToList());

    /// <summary>Copies the fields that were sent, then validates the final state. Returns an error message or null.</summary>
    private string? Apply(NotificationChannel c, NotificationChannelUpsert r)
    {
        if (r.Name is not null) c.Name = r.Name.Trim();
        if (r.Enabled is { } en) c.Enabled = en;
        if (r.MinSeverity is not null) c.MinSeverity = r.MinSeverity;
        if (r.NotifyResolved is { } nr) c.NotifyResolved = nr;
        if (r.OnlyFlagged is { } of) c.OnlyFlagged = of;
        if (r.Host is not null) c.Host = r.Host.Trim();
        if (r.Port is { } p) c.Port = p;
        if (r.StartTls is { } st) c.StartTls = st;
        if (r.Username is not null) c.Username = string.IsNullOrWhiteSpace(r.Username) ? null : r.Username.Trim();
        if (r.FromAddress is not null) c.FromAddress = r.FromAddress.Trim();
        if (r.Recipients is not null) c.Recipients = string.Join(", ", NotificationRules.SplitRecipients(r.Recipients));
        if (r.Url is not null) c.Url = r.Url.Trim();
        if (r.Format is not null) c.Format = r.Format;
        if (r.ClearSecret == true) c.Secret = null;
        if (!string.IsNullOrEmpty(r.Secret)) c.Secret = VncSecrets.Protect(r.Secret, Key);

        if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 100) return "name is required (max 100 characters)";
        if (!NotificationRules.Severities.Contains(c.MinSeverity)) return "min_severity must be info, warning or error";
        if (c.Kind == NotificationRules.Email)
            return NotificationRules.ValidateEmail(c.Host, c.Port, c.FromAddress, NotificationRules.SplitRecipients(c.Recipients))
                   ?? (c.Username is { Length: > 254 } ? "username is too long" : null);
        if (!NotificationRules.Formats.Contains(c.Format ?? "")) return "format must be json or slack";
        return NotificationRules.ValidateWebhookUrl(c.Url);
    }

    public async Task<Outcome<NotificationChannelDto>> CreateAsync(User caller, NotificationChannelUpsert req, CancellationToken ct = default)
    {
        if (req.Kind is null || !NotificationRules.Kinds.Contains(req.Kind)) return Outcome.Invalid<NotificationChannelDto>("kind must be email or webhook");
        var c = new NotificationChannel { Kind = req.Kind, CreatedBy = caller.Username };
        if (Apply(c, req) is { } err) return Outcome.Invalid<NotificationChannelDto>(err);
        if (await db.NotificationChannels.AnyAsync(x => x.Name == c.Name, ct)) return Outcome.Conflict<NotificationChannelDto>("A channel with this name already exists");
        db.NotificationChannels.Add(c);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "notification_channel", $"{caller.Username} added notification channel '{c.Name}' ({c.Kind})", ct: ct);
        return Outcome.Ok(ToDto(c));
    }

    public async Task<Outcome<NotificationChannelDto>> UpdateAsync(User caller, int id, NotificationChannelUpsert req, CancellationToken ct = default)
    {
        var c = await db.NotificationChannels.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Outcome.NotFound<NotificationChannelDto>();
        if (Apply(c, req) is { } err) return Outcome.Invalid<NotificationChannelDto>(err);
        if (await db.NotificationChannels.AnyAsync(x => x.Name == c.Name && x.Id != id, ct)) return Outcome.Conflict<NotificationChannelDto>("A channel with this name already exists");
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "notification_channel",
            $"{caller.Username} modified notification channel '{c.Name}'{(string.IsNullOrEmpty(req.Secret) ? "" : " (secret changed)")}", ct: ct);
        return Outcome.Ok(ToDto(c));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var c = await db.NotificationChannels.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Outcome.NotFound<bool>();
        db.NotificationChannels.Remove(c);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "notification_channel", $"{caller.Username} deleted notification channel '{c.Name}'", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<NotificationTestResult>> TestAsync(User caller, int id, CancellationToken ct = default)
    {
        var c = await db.NotificationChannels.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Outcome.NotFound<NotificationTestResult>();
        var sample = new AlertEvent(0, NotificationRules.Opened, AlertType.Custom, AlertSeverity.Warning, "Test notification from NetTactical", "TEST-PC", "Test client", "Test site", "test", DateTime.UtcNow, true);
        var secret = c.Secret is null ? null : VncSecrets.Unprotect(c.Secret, Key);
        var result = await NotificationWorker.RecordAsync(db, c, t => NotificationSender.SendAsync(c, secret, sample, t), ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "notification_channel", $"{caller.Username} sent a test notification to '{c.Name}': {(result.Ok ? "ok" : "failed")}", ct: ct);
        return Outcome.Ok(result);
    }
}
