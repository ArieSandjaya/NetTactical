using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class EventLogTests
{
    [Fact] public void ValidInputPassesAndBadInputIsNamed()
    {
        Assert.Null(EventLogs.Validate("system", "all", 24, 100, null, null));
        Assert.NotNull(EventLogs.Validate("Microsoft-Windows-X/Operational", "all", 24, 100, null, null));
        Assert.NotNull(EventLogs.Validate("System", "verbose", 24, 100, null, null));
        Assert.NotNull(EventLogs.Validate("System", "all", 0, 100, null, null));
        Assert.NotNull(EventLogs.Validate("System", "all", 24, 501, null, null));
        Assert.NotNull(EventLogs.Validate("System", "all", 24, 100, new string('a', 101), null));
        Assert.NotNull(EventLogs.Validate("System", "all", 24, 100, "a\nb", null));
        Assert.NotNull(EventLogs.Validate("System", "all", 24, 100, null, 70000));
    }

    [Fact] public void TheScriptFiltersAndEscapesTheSearchText()
    {
        var s = EventLogs.Script("system", "error", 6, 50, "it's [x]", 7036);
        Assert.Contains("LogName = 'System'", s); Assert.Contains("AddHours(-6)", s); Assert.Contains("$f.Level = 2", s); Assert.Contains("$f.Id = 7036", s);
        Assert.Contains("'it''s [x]'", s); Assert.Contains("WildcardPattern", s); Assert.Contains("-First 51", s);
        var plain = EventLogs.Script("Application", "all", 24, 100, null, null);
        Assert.DoesNotContain("$f.Level", plain); Assert.DoesNotContain("$pat", plain);
    }

    [Fact] public void ParsingKeepsTheNewestAndFlagsMore()
    {
        var json = """[{"time":"2024-05-01T10:00:00Z","id":7036,"level":"Information","provider":"SCM","message":"x","record":5},{"time":"2024-05-01T09:00:00Z","id":1,"level":"Error","provider":"P","message":"y","record":4},{"time":"2024-05-01T08:00:00Z","id":2,"level":"Warning","provider":"P","message":"z","record":3}]""";
        var r = EventLogs.Parse("System", 2, json)!;
        Assert.Equal(2, r.Entries.Count); Assert.True(r.Truncated); Assert.Equal(7036, r.Entries[0].Id); Assert.Equal(5, r.Entries[0].RecordId);
        Assert.False(EventLogs.Parse("System", 5, json)!.Truncated);
        Assert.Empty(EventLogs.Parse("System", 5, "[]")!.Entries);
        Assert.Null(EventLogs.Parse("System", 5, "{}")); Assert.Null(EventLogs.Parse("System", 5, "nope"));
    }
}
