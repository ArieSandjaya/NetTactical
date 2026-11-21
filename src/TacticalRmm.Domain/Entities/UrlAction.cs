namespace TacticalRmm.Domain.Entities;

/// <summary>A saved link with placeholders such as {{agent.hostname}}; opening it for an asset fills them in (a ticket search, a documentation page, a remote tool).</summary>
public class UrlAction
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Pattern { get; set; } = "";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
