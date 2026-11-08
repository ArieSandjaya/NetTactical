using System.Text.Json;
using System.Text.RegularExpressions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain;

namespace TacticalRmm.Application.Logic;

public static partial class TaskValidator
{
    public static readonly string[] SupportedTypes =
        [TaskType.Manual, TaskType.CheckFailure, TaskType.RunOnce, TaskType.Daily, TaskType.Weekly, TaskType.Monthly, TaskType.MonthlyDow];
    private static readonly string[] Scheduled = [TaskType.RunOnce, TaskType.Daily, TaskType.Weekly, TaskType.Monthly, TaskType.MonthlyDow];
    private static readonly string[] Severities = [AlertSeverity.Info, AlertSeverity.Warning, AlertSeverity.Error];
    private static readonly string[] Shells = [ScriptShell.PowerShell, ScriptShell.Cmd, ScriptShell.Python, ScriptShell.Shell, ScriptShell.Nushell, ScriptShell.Deno];

    [GeneratedRegex(@"^\d{1,4}[dhmsDHMS]$")] private static partial Regex DurationPattern();

    /// <summary>Validates schedule rules + common fields. <paramref name="t"/> already holds the final values (existing data + changes).</summary>
    public static string? Validate(TaskUpsert t, string taskType, DateTime nowUtc)
    {
        if (taskType == TaskType.Onboarding) return "Onboarding tasks are not supported yet";
        if (!SupportedTypes.Contains(taskType)) return $"Unknown task_type '{taskType}'";
        if (string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 255) return "name is required (max 255 characters)";
        if (t.AlertSeverity != null && !Severities.Contains(t.AlertSeverity)) return "Invalid alert_severity";
        if (t.TaskInstancePolicy is < 0 or > 3) return "task_instance_policy must be between 0 and 3";
        if (t.ExpireDate is { } ex && ex < nowUtc) return "Expires date/time is in the past";

        if (Scheduled.Contains(taskType) && t.RunTimeDate is null) return $"run_time_date is required for task_type '{taskType}'";

        switch (taskType)
        {
            case TaskType.Daily when t.DailyInterval is null or < 1 or > 365: return "daily_interval (1-365) is required";
            case TaskType.Weekly when t.WeeklyInterval is null or < 1 or > 52: return "weekly_interval (1-52) is required";
            case TaskType.Weekly when t.RunTimeBitWeekdays is null or < 1 or > 127: return "run_time_bit_weekdays (1-127) is required";
            case TaskType.Monthly when t.MonthlyMonthsOfYear is null or < 1 or > 4095: return "monthly_months_of_year (1-4095) is required";
            case TaskType.Monthly when t.MonthlyDaysOfMonth is null or < 1: return "monthly_days_of_month is required";
            case TaskType.MonthlyDow when t.RunTimeBitWeekdays is null or < 1 or > 127: return "run_time_bit_weekdays (1-127) is required";
            case TaskType.MonthlyDow when t.MonthlyMonthsOfYear is null or < 1 or > 4095: return "monthly_months_of_year (1-4095) is required";
            case TaskType.MonthlyDow when t.MonthlyWeeksOfMonth is null or < 1 or > 31: return "monthly_weeks_of_month (1-31) is required";
            case TaskType.CheckFailure when t.AssignedCheck is null: return "assigned_check is required for a check failure task";
        }

        foreach (var d in new[] { t.TaskRepetitionDuration, t.TaskRepetitionInterval, t.RandomTaskDelay })
            if (!string.IsNullOrEmpty(d) && !DurationPattern().IsMatch(d)) return $"Invalid duration '{d}' (use forms like 30m, 1h, 2d)";
        return null;
    }

    /// <summary>Port TaskSerializer.validate_actions.</summary>
    public static string? ValidateActions(JsonElement actions)
    {
        if (actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() == 0) return "There must be at least one action configured";
        if (actions.GetArrayLength() > 50) return "Too many actions (max 50)";

        foreach (var a in actions.EnumerateArray())
        {
            if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                return "Each action must have a type field of either 'script' or 'cmd'";

            if (!a.TryGetProperty("timeout", out var to) || to.ValueKind != JsonValueKind.Number || !to.TryGetInt32(out var seconds) || seconds is < 1 or > 86400)
                return "Each action needs a 'timeout' between 1 and 86400 seconds";

            switch (type.GetString())
            {
                case "script":
                    if (!a.TryGetProperty("script", out var sid) || sid.ValueKind != JsonValueKind.Number)
                        return "A script action type must have a 'script' field with primary key of script";
                    if (!a.TryGetProperty("script_args", out var args) || args.ValueKind != JsonValueKind.Array)
                        return "A script action type must have a 'script_args' field with an array of arguments";
                    break;
                case "cmd":
                    if (!a.TryGetProperty("command", out var c) || c.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(c.GetString()))
                        return "A command action type must have a non-empty 'command' field";
                    if (!a.TryGetProperty("shell", out var sh) || sh.ValueKind != JsonValueKind.String || !Shells.Contains(sh.GetString()))
                        return "A command action type must have a valid 'shell' field";
                    break;
                default:
                    return "Each action must have a type field of either 'script' or 'cmd'";
            }
        }
        return null;
    }
}
