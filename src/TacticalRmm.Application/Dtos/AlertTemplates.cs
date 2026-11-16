namespace TacticalRmm.Application.Dtos;

public class AlertTemplateUpsert
{
    public string? Name { get; set; }
    public bool? Enabled { get; set; }
    public bool? IsDefault { get; set; }
    public bool? FlagAgentOverdue { get; set; }
    public bool? FlagCheckFailures { get; set; }
    public bool? FlagTaskFailures { get; set; }
}

public record AlertTemplateDto(int Id, string Name, bool Enabled, bool IsDefault, bool FlagAgentOverdue, bool FlagCheckFailures, bool FlagTaskFailures,
    int ClientCount, int SiteCount);
