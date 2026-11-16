namespace TacticalRmm.Application.Dtos;

public class SnippetUpsert
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Code { get; set; }
}

/// <summary>Code is only sent by the detail call, not by the list.</summary>
public record SnippetDto(int Id, string Name, string? Description, string? Code, int UsedBy);
