using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;
using Xunit;

namespace TacticalRmm.Tests;

public class WinUpdateRulesTests
{
    private static EffectivePatch E(string freq, int hour = 3, int weekdays = 0, int day = 1) => new("approve", "approve", "inherit", "ignore", "inherit", freq, hour, weekdays, day, "never");

    [Fact]
    public void AgentSettingWinsAndInheritFallsThroughToPolicy()
    {
        var agent = new WinUpdatePolicy { Critical = "ignore", Important = "inherit", RunTimeFrequency = "inherit" };
        var pol = new WinUpdatePolicy { Critical = "approve", Important = "approve", RunTimeFrequency = "daily", RunTimeHour = 22, RebootAfterInstall = "required" };
        var e = WinUpdateRules.Resolve(agent, pol);
        Assert.True(e.Critical == "ignore" && e.Important == "approve", "per-field override");
        Assert.True(e.Frequency == "daily" && e.Hour == 22 && e.Reboot == "required", "schedule and reboot from policy");
    }

    [Fact]
    public void UnresolvedMeansDoNothingAutomatically()
    {
        var e = WinUpdateRules.Resolve(null, null);
        Assert.True(e.Frequency == "never" && e.Critical == "inherit" && e.Reboot == "never", "defaults");
        Assert.True(WinUpdateRules.AutoAction(e, "Critical") == "nothing", "no rule, no action");
    }

    [Fact]
    public void AutoActionFollowsSeverity()
    {
        var e = E("daily");
        Assert.True(WinUpdateRules.AutoAction(e, "Critical") == "approve" && WinUpdateRules.AutoAction(e, "important") == "approve", "approve");
        Assert.True(WinUpdateRules.AutoAction(e, "Low") == "ignore", "ignore");
        Assert.True(WinUpdateRules.AutoAction(e, "Moderate") == "nothing" && WinUpdateRules.AutoAction(e, null) == "nothing" && WinUpdateRules.AutoAction(e, "Weird") == "nothing", "undecided");
    }

    [Fact]
    public void ScheduleIsDueOnlyInTheHourOncePerWindow()
    {
        var d = new DateTime(2026, 11, 15, 3, 20, 0);       // Sunday
        Assert.True(WinUpdateRules.IsDue(E("daily"), d, null), "daily due");
        Assert.True(!WinUpdateRules.IsDue(E("daily"), d.AddHours(1), null), "wrong hour");
        Assert.True(!WinUpdateRules.IsDue(E("daily"), d, new DateTime(2026, 11, 15, 3, 5, 0)), "already ran this hour");
        Assert.True(WinUpdateRules.IsDue(E("daily"), d, new DateTime(2026, 11, 14, 3, 5, 0)), "ran yesterday");
        Assert.True(!WinUpdateRules.IsDue(E("never"), d, null) && !WinUpdateRules.IsDue(E("inherit"), d, null), "never / unresolved");
    }

    [Fact]
    public void WeeklyAndMonthlyDays()
    {
        var sunday = new DateTime(2026, 11, 15, 3, 0, 0);
        Assert.True(WinUpdateRules.IsDue(E("weekly", weekdays: 64), sunday, null), "Sunday bit 64");
        Assert.True(!WinUpdateRules.IsDue(E("weekly", weekdays: 1), sunday, null), "Monday only");
        Assert.True(WinUpdateRules.IsDue(E("weekly", weekdays: 1), new DateTime(2026, 11, 16, 3, 0, 0), null), "Monday bit 1");
        Assert.True(WinUpdateRules.IsDue(E("monthly", day: 15), sunday, null), "day 15");
        Assert.True(!WinUpdateRules.IsDue(E("monthly", day: 14), sunday, null), "not day 14");
        Assert.True(WinUpdateRules.IsDue(E("monthly", day: 31), new DateTime(2026, 2, 28, 3, 0, 0), null), "day 31 clamps to the last day of February");
    }

    [Fact]
    public void ValidationRejectsBadValues()
    {
        Assert.True(WinUpdateRules.Validate("approve", "ignore", "inherit", "inherit", "inherit", "daily", 3, 0, 1, "never") is null, "valid");
        Assert.True(WinUpdateRules.Validate("yes", "ignore", "inherit", "inherit", "inherit", "daily", 3, 0, 1, "never") is not null, "bad approval");
        Assert.True(WinUpdateRules.Validate("approve", "ignore", "inherit", "inherit", "inherit", "hourly", 3, 0, 1, "never") is not null, "bad frequency");
        Assert.True(WinUpdateRules.Validate("approve", "ignore", "inherit", "inherit", "inherit", "daily", 24, 0, 1, "never") is not null, "bad hour");
        Assert.True(WinUpdateRules.Validate("approve", "ignore", "inherit", "inherit", "inherit", "weekly", 3, 0, 1, "never") is not null, "weekly needs a day");
        Assert.True(WinUpdateRules.Validate("approve", "ignore", "inherit", "inherit", "inherit", "monthly", 3, 0, 32, "never") is not null, "bad day");
        Assert.True(WinUpdateRules.Validate("approve", "ignore", "inherit", "inherit", "inherit", "daily", 3, 0, 1, "sometimes") is not null, "bad reboot");
    }

    [Fact]
    public void UnknownTimeZoneFallsBackToUtc()
    {
        Assert.True(WinUpdateRules.ZoneOf("Not/AZone") == TimeZoneInfo.Utc && WinUpdateRules.ZoneOf(null) == TimeZoneInfo.Utc, "fallback");
    }
}
