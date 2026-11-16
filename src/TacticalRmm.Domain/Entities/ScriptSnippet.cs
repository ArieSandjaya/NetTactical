namespace TacticalRmm.Domain.Entities;

/// <summary>A reusable piece of script. A script that contains {{name}} gets the snippet's code in its place when it runs.</summary>
public class ScriptSnippet : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Code { get; set; } = "";
}
