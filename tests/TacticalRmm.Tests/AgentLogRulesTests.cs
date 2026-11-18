using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class AgentLogRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void AnEmptyNoteIsRefused(string? s) => Assert.Null(AgentLogRules.CleanNote(s));

    [Fact]
    public void ANoteIsTrimmedAndCut()
    {
        Assert.Equal("RAM replaced", AgentLogRules.CleanNote("  RAM replaced \n"));
        Assert.Equal(AgentLogRules.MaxNote, AgentLogRules.CleanNote(new string('x', 9000))!.Length);
    }

    [Fact]
    public void HistorySizeIsClamped()
    {
        Assert.Equal(50, AgentLogRules.Take(null));
        Assert.Equal(1, AgentLogRules.Take(-5));
        Assert.Equal(AgentLogRules.MaxHistory, AgentLogRules.Take(100000));
    }

    [Fact]
    public void ACommandIsCutForTheHistory() => Assert.Equal(AgentLogRules.MaxCommand + 1, AgentLogRules.CommandText(new string('c', 5000)).Length);

    [Fact]
    public void AScriptResultIsRead()
    {
        var (o, e, rc, ex) = AgentLogRules.ParseResults("{\"stdout\":\" done \",\"stderr\":\"\",\"retcode\":2,\"execution_time\":1.5}");
        Assert.Equal("done", o); Assert.Null(e); Assert.Equal(2, rc); Assert.Equal(1.5, ex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"stdout\":5,\"retcode\":\"x\"}")]
    public void AnUnreadableResultGivesNothing(string? json) => Assert.Equal((null, null, null, null), AgentLogRules.ParseResults(json));
}
