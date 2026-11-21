namespace TacticalRmm.Application.Dtos;

public sealed class MaintenanceRequest
{
    public string Action { get; set; } = "";
    public int Days { get; set; }
}

/// <summary>How many records the action matches (preview) or removed (run).</summary>
public sealed record MaintenanceResult(string Action, int Days, int Count, bool Done);
