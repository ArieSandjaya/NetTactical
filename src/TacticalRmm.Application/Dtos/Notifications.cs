namespace TacticalRmm.Application.Dtos;

public class NotificationChannelUpsert
{
    public string? Name { get; set; }
    /// <summary>email | webhook (only on create)</summary>
    public string? Kind { get; set; }
    public bool? Enabled { get; set; }
    public string? MinSeverity { get; set; }
    public bool? NotifyResolved { get; set; }
    public bool? OnlyFlagged { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public bool? StartTls { get; set; }
    public string? Username { get; set; }
    public string? FromAddress { get; set; }
    /// <summary>Comma, semicolon or newline separated.</summary>
    public string? Recipients { get; set; }
    public string? Url { get; set; }
    public string? Format { get; set; }
    /// <summary>SMTP password / webhook signing secret. Empty or missing keeps the current one.</summary>
    public string? Secret { get; set; }
    public bool? ClearSecret { get; set; }
}

public record NotificationChannelDto(int Id, string Name, string Kind, bool Enabled, string MinSeverity, bool NotifyResolved, bool OnlyFlagged,
    string? Host, int Port, bool StartTls, string? Username, string? FromAddress, string? Recipients, string? Url, string? Format, bool HasSecret,
    DateTime? LastAttempt, bool? LastOk, string? LastError);

public record NotificationTestResult(bool Ok, string? Error);
