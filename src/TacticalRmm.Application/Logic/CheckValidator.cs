using System.Text.RegularExpressions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain;

namespace TacticalRmm.Application.Logic;

public static partial class CheckValidator
{
    private static readonly string[] Types =
        [CheckType.DiskSpace, CheckType.Ping, CheckType.CpuLoad, CheckType.Memory, CheckType.WinSvc, CheckType.Script, CheckType.EventLog];
    private static readonly string[] Severities = [AlertSeverity.Info, AlertSeverity.Warning, AlertSeverity.Error];

    [GeneratedRegex(@"^[A-Za-z]:?$")] private static partial Regex DiskPattern();
    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.\-]{0,251}[A-Za-z0-9])?$|^[0-9a-fA-F:]+$")] private static partial Regex HostPattern();

    public static bool IsKnownType(string? t) => t != null && Types.Contains(t);

    /// <returns>error message, or null if valid. <paramref name="merged"/> = final values after merging with the existing data.</returns>
    public static string? Validate(string checkType, CheckUpsert merged)
    {
        if (!IsKnownType(checkType)) return $"Unknown check_type '{checkType}'";
        if (merged.AlertSeverity != null && !Severities.Contains(merged.AlertSeverity)) return "Invalid alert_severity";
        if (merged.FailsB4Alert is < 1 or > 255) return "fails_b4_alert must be between 1 and 255";
        if (merged.RunInterval is < 0 or > 86400 || merged.RunInterval is > 0 and < 10) return "run_interval must be 0 (agent default) or between 10 and 86400 seconds";

        switch (checkType)
        {
            case CheckType.DiskSpace:
                if (string.IsNullOrWhiteSpace(merged.Disk) || !DiskPattern().IsMatch(merged.Disk)) return "disk must be a drive letter like 'C:'";
                return Thresholds(merged, higherIsWorse: false);
            case CheckType.CpuLoad:
            case CheckType.Memory:
                return Thresholds(merged, higherIsWorse: true);
            case CheckType.Ping:
                return string.IsNullOrWhiteSpace(merged.Ip) || !HostPattern().IsMatch(merged.Ip) ? "ip must be a valid hostname or IP address" : null;
            case CheckType.WinSvc:
                return string.IsNullOrWhiteSpace(merged.SvcName) ? "svc_name is required" : null;
            case CheckType.Script:
                if (merged.Script is null or <= 0) return "script is required";
                if (merged.Timeout is null or < 1 or > 86400) return "timeout must be between 1 and 86400 seconds";
                return null;
            case CheckType.EventLog:
                if (string.IsNullOrWhiteSpace(merged.LogName)) return "log_name is required";
                if (merged.EventId is < 0) return "event_id is invalid";
                if (merged.SearchLastDays is null or < 1 or > 3650) return "search_last_days must be between 1 and 3650";
                return null;
        }
        return null;
    }

    private static string? Thresholds(CheckUpsert c, bool higherIsWorse)
    {
        var e = c.ErrorThreshold ?? 0; var w = c.WarningThreshold ?? 0;
        if (e is < 0 or > 99 || w is < 0 or > 99) return "Thresholds must be between 0 and 99";
        if (e == 0 && w == 0) return "At least one of error_threshold or warning_threshold is required";
        if (e > 0 && w > 0)
        {
            if (higherIsWorse && w >= e) return "Warning threshold must be less than error threshold";
            if (!higherIsWorse && w <= e) return "Warning threshold must be greater than error threshold";
        }
        return null;
    }

    public static string DefaultName(string checkType, CheckUpsert c, string? scriptName) => checkType switch
    {
        CheckType.DiskSpace => $"Disk Space Drive {c.Disk} - Free Space Below Threshold",
        CheckType.Ping => $"Ping {c.Ip}",
        CheckType.CpuLoad => "CPU Load",
        CheckType.Memory => "Memory",
        CheckType.WinSvc => $"Service - {c.SvcDisplayName ?? c.SvcName}",
        CheckType.Script => $"Script - {scriptName ?? c.Script?.ToString()}",
        CheckType.EventLog => $"Event Log - {c.LogName}",
        _ => checkType
    };
}
