using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using Xunit;

namespace TacticalRmm.Tests;

public class AlertTemplateRulesTests
{
    private static TemplateRef On(int id) => new(id, true);
    private static TemplateRef Off(int id) => new(id, false);

    [Fact]
    public void SiteBeatsClientBeatsGlobal()
    {
        Assert.Equal(1, AlertTemplateRules.Pick(On(1), On(2), On(3)));
        Assert.Equal(2, AlertTemplateRules.Pick(null, On(2), On(3)));
        Assert.Equal(3, AlertTemplateRules.Pick(null, null, On(3)));
    }

    [Fact]
    public void NothingAssignedMeansNoTemplate()
    {
        Assert.Null(AlertTemplateRules.Pick(null, null, null));
    }

    [Fact]
    public void DisabledTemplateIsSkippedSoTheNextLevelStillApplies()
    {
        Assert.Equal(2, AlertTemplateRules.Pick(Off(1), On(2), On(3)));
        Assert.Equal(3, AlertTemplateRules.Pick(Off(1), Off(2), On(3)));
        Assert.Null(AlertTemplateRules.Pick(Off(1), Off(2), Off(3)));
    }

    [Fact]
    public void TemplateFlagsOnlyTheKindsItAsksFor()
    {
        var t = new AlertFlags(AgentOverdue: false, CheckFailures: true, TaskFailures: false);
        Assert.True(AlertTemplateRules.Flag(AlertType.Check, false, t));
        Assert.False(AlertTemplateRules.Flag(AlertType.Task, false, t));
        Assert.False(AlertTemplateRules.Flag(AlertType.Availability, false, t));
    }

    [Fact]
    public void TemplateNeverRemovesTheFlagTheItemAlreadyHas()
    {
        var none = new AlertFlags(false, false, false);
        Assert.True(AlertTemplateRules.Flag(AlertType.Task, true, none));
        Assert.True(AlertTemplateRules.Flag(AlertType.Task, true, null));
    }

    [Fact]
    public void WithoutATemplateOnlyTheItemFlagCounts()
    {
        Assert.False(AlertTemplateRules.Flag(AlertType.Check, false, null));
    }

    [Fact]
    public void CustomAlertsAreNotAffectedByATemplate()
    {
        var all = new AlertFlags(true, true, true);
        Assert.False(AlertTemplateRules.Flag(AlertType.Custom, false, all));
    }
}
