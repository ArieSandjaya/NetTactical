namespace TacticalRmm.Application.Dtos;

public sealed class UrlActionRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Pattern { get; set; } = "";
}

public sealed class UrlActionRunRequest
{
    public string AgentId { get; set; } = "";
}

public sealed record UrlActionDto(int Id, string Name, string? Description, string Pattern);
public sealed record UrlActionRunDto(string Url);
