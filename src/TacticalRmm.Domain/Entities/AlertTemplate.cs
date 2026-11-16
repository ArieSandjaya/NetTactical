namespace TacticalRmm.Domain.Entities;

/// <summary>
/// Simple alert template: "always flag these kinds of alerts for notification" for every agent of a client or site
/// (or for everyone when it is the default template). It adds to the flags a check, task or agent already has.
/// </summary>
public class AlertTemplate : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Applies to agents whose site and client have no template of their own. At most one template is the default.</summary>
    public bool IsDefault { get; set; }
    public bool FlagAgentOverdue { get; set; }
    public bool FlagCheckFailures { get; set; }
    public bool FlagTaskFailures { get; set; }
}
