using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class SoftwareListTests
{
    [Fact]
    public void ReadsANatsReplyOfMaps()
    {
        object reply = new List<object?>
        {
            new Dictionary<object, object?> { ["name"] = "Zoom", ["version"] = "5.1", ["publisher"] = "Zoom Video", ["install_date"] = "20260101", ["size"] = 123456L },
            new Dictionary<object, object?> { ["name"] = "7-Zip", ["version"] = "23.01" },
            new Dictionary<object, object?> { ["version"] = "no name, skipped" },
            "not a map",
        };
        var items = SoftwareList.FromReply(reply)!;
        Assert.Equal(new[] { "7-Zip", "Zoom" }, items.Select(i => i.Name).ToArray());   // sorted by name
        Assert.Equal("123456", items[1].Size);
        Assert.Null(items[0].Publisher);
    }

    [Fact]
    public void ReplyThatIsNotAListIsNull()
    {
        Assert.Null(SoftwareList.FromReply("timeout"));
        Assert.Null(SoftwareList.FromReply(null));
        Assert.Null(SoftwareList.FromReply(new Dictionary<object, object?> { ["name"] = "x" }));
    }

    [Fact]
    public void JsonRoundTripKeepsOnlyKnownFields()
    {
        var json = """[{"name":"App","version":"1.0","publisher":"P","install_date":"2026-01-01","size":"1 MB","location":"C:\\x","uninstall":"x.exe"},{"name":" "},{"name":"Other","size":42}]""";
        var items = SoftwareList.ParseJson(json);
        Assert.Equal(new[] { "App", "Other" }, items.Select(i => i.Name).ToArray());
        Assert.Equal("42", items[1].Size);
        var back = SoftwareList.ToJson(items);
        Assert.DoesNotContain("location", back);
        Assert.DoesNotContain("uninstall", back);
        Assert.Equal(items, SoftwareList.ParseJson(back));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"name\":\"x\"}")]
    [InlineData("[1,2,3]")]
    public void GarbageGivesAnEmptyList(string json) => Assert.Empty(SoftwareList.ParseJson(json));

    [Fact]
    public void LongTextAndHugeListsAreCut()
    {
        var items = SoftwareList.ParseJson($"[{{\"name\":\"{new string('a', 1000)}\"}}]");
        Assert.Equal(SoftwareList.MaxText, items[0].Name.Length);
        var many = "[" + string.Join(",", Enumerable.Range(0, SoftwareList.MaxItems + 50).Select(i => $"{{\"name\":\"n{i}\"}}")) + "]";
        Assert.Equal(SoftwareList.MaxItems, SoftwareList.ParseJson(many).Count);
    }

    [Fact]
    public void FilterMatchesNamePublisherAndVersionIgnoringCase()
    {
        var items = SoftwareList.ParseJson("""[{"name":"Google Chrome","version":"120.0","publisher":"Google LLC"},{"name":"7-Zip","version":"23.01","publisher":"Igor Pavlov"}]""");
        Assert.Single(SoftwareList.Filter(items, "chrome"));
        Assert.Single(SoftwareList.Filter(items, "pavlov"));
        Assert.Single(SoftwareList.Filter(items, "23.01"));
        Assert.Equal(2, SoftwareList.Filter(items, "  ").Count());
        Assert.Empty(SoftwareList.Filter(items, "firefox"));
    }
}
