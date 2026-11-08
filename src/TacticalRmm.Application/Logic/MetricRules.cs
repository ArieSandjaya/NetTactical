using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain;

namespace TacticalRmm.Application.Logic;

public static class MetricRules
{
    public static readonly string[] Types = [CheckType.CpuLoad, CheckType.Memory, CheckType.DiskSpace];

    /// <summary>Percent used (0-100) from the check payload, or null if it is not a metric / has no data.</summary>
    public static double? UsedPercent(string checkType, CheckResultPayload p)
    {
        double? v = checkType switch
        {
            CheckType.CpuLoad or CheckType.Memory => p.Percent,
            CheckType.DiskSpace when p.Exists == true => p.PercentUsed,
            _ => null
        };
        if (v is null || double.IsNaN(v.Value)) return null;
        return Math.Round(Math.Clamp(v.Value, 0, 100), 1);
    }

    /// <summary>Short name in the API -> check type. Returns null if unknown.</summary>
    public static string? FromApiName(string? name) => name?.ToLowerInvariant() switch
    {
        "cpu" => CheckType.CpuLoad,
        "memory" or "ram" => CheckType.Memory,
        "disk" => CheckType.DiskSpace,
        _ => null
    };

    public static string ApiName(string checkType) => checkType switch
    {
        CheckType.CpuLoad => "cpu", CheckType.Memory => "memory", CheckType.DiskSpace => "disk", _ => checkType
    };
}
