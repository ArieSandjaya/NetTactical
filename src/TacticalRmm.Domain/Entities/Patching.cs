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

/// <summary>Kebijakan patch per agent atau per Policy. Nilai: inherit | never | required | always (reboot).</summary>
public class WinUpdatePolicy : AuditableEntity
{
    public int Id { get; set; }
    public int? AgentId { get; set; }
    public int? PolicyId { get; set; }
    public string RebootAfterInstall { get; set; } = "inherit";
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
