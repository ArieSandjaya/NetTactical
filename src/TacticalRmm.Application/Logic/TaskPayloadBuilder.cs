using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Logic;

/// <summary>Port AutomatedTask.generate_nats_task_payload — contents of "schedtaskpayload" for the Windows Task Scheduler on the agent.</summary>
public static class TaskPayloadBuilder
{
    /// <summary>Port convert_to_iso_duration (including the "P1DT" form exactly as in Django).</summary>
    public static string ToIsoDuration(string s)
    {
        var t = s.ToUpperInvariant();
        return t.Contains('D') ? $"P{t.Replace("D", "DT")}" : $"PT{t}";
    }

    public static Dictionary<string, object?> Build(AutomatedTask t, DateTime nowUtc)
    {
        var task = new Dictionary<string, object?>
        {
            ["pk"] = t.Id,
            ["type"] = "rmm",
            ["name"] = t.WinTaskName,
            ["overwrite_task"] = true,
            ["enabled"] = t.Enabled,
            ["trigger"] = t.TaskType != TaskType.CheckFailure ? t.TaskType : TaskType.Manual,
            ["multiple_instances"] = t.TaskInstancePolicy,
            ["delete_expired_task_after"] = t.ExpireDate is not null ? t.RemoveIfNotScheduled : false,
            ["start_when_available"] = t.TaskType == TaskType.Manual ? false : t.TaskType == TaskType.RunOnce ? true : t.RunAsapAfterMissed,
        };

        if (t.TaskType is TaskType.Daily or TaskType.Weekly or TaskType.Monthly or TaskType.MonthlyDow or TaskType.RunOnce)
        {
            var start = (t.RunTimeDate ?? nowUtc).ToUniversalTime();
            task["start_year"] = start.Year; task["start_month"] = start.Month; task["start_day"] = start.Day;
            task["start_hour"] = start.Hour; task["start_min"] = start.Minute;

            if (t.ExpireDate is { } e)
            {
                e = e.ToUniversalTime();
                task["expire_year"] = e.Year; task["expire_month"] = e.Month; task["expire_day"] = e.Day;
                task["expire_hour"] = e.Hour; task["expire_min"] = e.Minute;
            }
            if (!string.IsNullOrEmpty(t.RandomTaskDelay)) task["random_delay"] = ToIsoDuration(t.RandomTaskDelay);
            if (!string.IsNullOrEmpty(t.TaskRepetitionInterval) && !string.IsNullOrEmpty(t.TaskRepetitionDuration))
            {
                task["repetition_interval"] = ToIsoDuration(t.TaskRepetitionInterval);
                task["repetition_duration"] = ToIsoDuration(t.TaskRepetitionDuration);
                task["stop_at_duration_end"] = t.StopTaskAtDurationEnd;
            }

            switch (t.TaskType)
            {
                case TaskType.Daily:
                    task["day_interval"] = t.DailyInterval;
                    break;
                case TaskType.Weekly:
                    task["week_interval"] = t.WeeklyInterval;
                    task["days_of_week"] = t.RunTimeBitWeekdays;
                    break;
                case TaskType.Monthly:
                    var days = t.MonthlyDaysOfMonth ?? 0;
                    var last = days >= 0x80000000L;   // bit 31 = "last day of the month"
                    task["days_of_month"] = last ? days - 0x80000000L : days;
                    task["run_on_last_day_of_month"] = last;
                    task["months_of_year"] = t.MonthlyMonthsOfYear;
                    break;
                case TaskType.MonthlyDow:
                    task["days_of_week"] = t.RunTimeBitWeekdays;
                    task["months_of_year"] = t.MonthlyMonthsOfYear;
                    task["weeks_of_month"] = t.MonthlyWeeksOfMonth;
                    break;
            }
        }
        return task;
    }
}
