namespace TacticalRmm.Domain.Entities;

public class WinUpdate
{
    public long Id { get; set; }
    public int AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    public string? Guid { get; set; }
    public string? Kb { get; set; }
    public string? Title { get; set; }
    public bool Installed { get; set; }
    public bool Downloaded { get; set; }
    public string? Description { get; set; }
    public string? Severity { get; set; }
    public List<string> Categories { get; set; } = new();
    public List<string> CategoryIds { get; set; } = new();
    public List<string> KbArticleIds { get; set; } = new();
    public List<string> MoreInfoUrls { get; set; } = new();
    public string? SupportUrl { get; set; }
    public int? RevisionNumber { get; set; }
    public string Action { get; set; } = "nothing";
    public string Result { get; set; } = "n/a";
    public DateTime? DateInstalled { get; set; }
}

/// <summary>Patch policy per agent or per Policy. Values: inherit | never | required | always (reboot).</summary>
public class WinUpdatePolicy : AuditableEntity
{
    public int Id { get; set; }
    public int? AgentId { get; set; }
    public int? PolicyId { get; set; }
    public string RebootAfterInstall { get; set; } = "inherit";
    // Auto-approval by severity: inherit | approve | ignore. "Other" covers updates without a known severity.
    public string Critical { get; set; } = "inherit";
    public string Important { get; set; } = "inherit";
    public string Moderate { get; set; } = "inherit";
    public string Low { get; set; } = "inherit";
    public string Other { get; set; } = "inherit";
    // Install schedule: inherit | never | daily | weekly | monthly, at RunTimeHour (agent's local time).
    public string RunTimeFrequency { get; set; } = "inherit";
    public int RunTimeHour { get; set; } = 3;
    /// <summary>Weekly: bit mask, Monday = 1 ... Sunday = 64.</summary>
    public int RunTimeWeekdays { get; set; }
    /// <summary>Monthly: day of month (1-31; a day past the end of the month runs on the last day).</summary>
    public int RunTimeDay { get; set; } = 1;
    public DateTime? LastRunAt { get; set; }
}

public class InstalledSoftware
{
    public long Id { get; set; }
    public int AgentId { get; set; }
    public string Software { get; set; } = "[]"; // jsonb
}

public class AgentNote
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public int? UserId { get; set; }
    public string? Note { get; set; }
    public DateTime EntryTime { get; set; }
}
