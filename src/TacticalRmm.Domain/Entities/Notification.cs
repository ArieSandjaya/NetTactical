namespace TacticalRmm.Domain.Entities;

/// <summary>Where alert notifications are delivered: an SMTP mailbox list ("email") or an HTTP endpoint ("webhook").</summary>
public class NotificationChannel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "email";
    public bool Enabled { get; set; } = true;
    /// <summary>info | warning | error: alerts below this severity are not sent.</summary>
    public string MinSeverity { get; set; } = AlertSeverity.Warning;
    public bool NotifyResolved { get; set; } = true;
    /// <summary>Send only for checks/tasks/agents that have their email alert flag turned on.</summary>
    public bool OnlyFlagged { get; set; }

    // email
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool StartTls { get; set; } = true;
    public string? Username { get; set; }
    public string? FromAddress { get; set; }
    /// <summary>Comma separated.</summary>
    public string? Recipients { get; set; }

    // webhook
    public string? Url { get; set; }
    /// <summary>json | slack</summary>
    public string? Format { get; set; } = "json";

    /// <summary>AES-GCM protected SMTP password or webhook signing secret. Never returned by the API.</summary>
    public string? Secret { get; set; }

    public DateTime? LastAttempt { get; set; }
    public bool? LastOk { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}
