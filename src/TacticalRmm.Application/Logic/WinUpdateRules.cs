using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Logic;

/// <summary>Effective Windows Update settings after agent-over-policy inheritance.</summary>
public record EffectivePatch(string Critical, string Important, string Moderate, string Low, string Other, string Frequency, int Hour, int Weekdays, int Day, string Reboot);

/// <summary>Pure rules for Windows Update approval and scheduling.</summary>
public static class WinUpdateRules
{
    public static readonly string[] Approvals = ["inherit", "approve", "ignore"];
    public static readonly string[] Frequencies = ["inherit", "never", "daily", "weekly", "monthly"];
    public static readonly string[] Reboots = ["inherit", "never", "required", "always"];
    public static readonly string[] Actions = ["approve", "ignore", "nothing"];

    private static string Pick(string? own, string? parent, string fallback) =>
        !string.IsNullOrEmpty(own) && own != "inherit" ? own : !string.IsNullOrEmpty(parent) && parent != "inherit" ? parent : fallback;

    /// <summary>A setting on the agent wins; "inherit" falls through to the policy; still unresolved means: do nothing automatically.</summary>
    public static EffectivePatch Resolve(WinUpdatePolicy? agent, WinUpdatePolicy? policy)
    {
        var freq = Pick(agent?.RunTimeFrequency, policy?.RunTimeFrequency, "never");
        // The schedule fields belong together: take them from the same level that supplied the frequency.
        var src = agent is not null && agent.RunTimeFrequency != "inherit" ? agent : policy;
        return new EffectivePatch(
            Pick(agent?.Critical, policy?.Critical, "inherit"), Pick(agent?.Important, policy?.Important, "inherit"),
            Pick(agent?.Moderate, policy?.Moderate, "inherit"), Pick(agent?.Low, policy?.Low, "inherit"), Pick(agent?.Other, policy?.Other, "inherit"),
            freq, src?.RunTimeHour ?? 3, src?.RunTimeWeekdays ?? 0, src?.RunTimeDay ?? 1, Pick(agent?.RebootAfterInstall, policy?.RebootAfterInstall, "never"));
    }

    /// <summary>The Action a newly reported update gets: approve / ignore, or "nothing" when no rule covers its severity.</summary>
    public static string AutoAction(EffectivePatch e, string? severity)
    {
        var rule = severity?.ToLowerInvariant() switch { "critical" => e.Critical, "important" => e.Important, "moderate" => e.Moderate, "low" => e.Low, _ => e.Other };
        return rule is "approve" or "ignore" ? rule : "nothing";
    }

    /// <summary>True during the scheduled hour on a scheduled day, once per hour window.</summary>
    public static bool IsDue(EffectivePatch e, DateTime localNow, DateTime? lastRunLocal)
    {
        if (e.Frequency is not ("daily" or "weekly" or "monthly") || localNow.Hour != e.Hour) return false;
        var dayOk = e.Frequency switch
        {
            "daily" => true,
            "weekly" => (e.Weekdays & (1 << (((int)localNow.DayOfWeek + 6) % 7))) != 0,
            _ => localNow.Day == Math.Min(Math.Max(e.Day, 1), DateTime.DaysInMonth(localNow.Year, localNow.Month)),
        };
        if (!dayOk) return false;
        var windowStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 0, 0);
        return lastRunLocal is null || lastRunLocal < windowStart;
    }

    /// <summary>Null when valid.</summary>
    public static string? Validate(string critical, string important, string moderate, string low, string other, string frequency, int hour, int weekdays, int day, string reboot)
    {
        foreach (var (n, v) in new[] { ("critical", critical), ("important", important), ("moderate", moderate), ("low", low), ("other", other) })
            if (!Approvals.Contains(v)) return $"{n} must be inherit, approve or ignore";
        if (!Frequencies.Contains(frequency)) return "frequency must be inherit, never, daily, weekly or monthly";
        if (!Reboots.Contains(reboot)) return "reboot must be inherit, never, required or always";
        if (hour is < 0 or > 23) return "hour must be 0-23";
        if (weekdays is < 0 or > 127) return "weekdays must be a mask of 0-127";
        if (frequency == "weekly" && weekdays == 0) return "pick at least one weekday for a weekly schedule";
        if (day is < 1 or > 31) return "day of month must be 1-31";
        return null;
    }

    public static TimeZoneInfo ZoneOf(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { } }
        return TimeZoneInfo.Utc;
    }
}
