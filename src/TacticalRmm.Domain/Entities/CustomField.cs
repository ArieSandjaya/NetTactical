namespace TacticalRmm.Domain.Entities;

/// <summary>A field an administrator defines once for clients, sites or agents. Values live in <see cref="CustomFieldValue"/>.</summary>
public class CustomField
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>"client", "site" or "agent".</summary>
    public string Model { get; set; } = "";
    /// <summary>"text", "number", "checkbox", "datetime" or "single" (pick one of Options).</summary>
    public string Type { get; set; } = "text";
    /// <summary>For "single": the allowed choices, one per line.</summary>
    public string? Options { get; set; }
    /// <summary>Shown when a client, site or agent has no value of its own.</summary>
    public string? DefaultValue { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedTime { get; set; }
}

/// <summary>The value of one field for one client, site or agent (TargetId is that row's database id).</summary>
public class CustomFieldValue
{
    public int Id { get; set; }
    public int FieldId { get; set; }
    public CustomField Field { get; set; } = null!;
    public int TargetId { get; set; }
    public string Value { get; set; } = "";
}
