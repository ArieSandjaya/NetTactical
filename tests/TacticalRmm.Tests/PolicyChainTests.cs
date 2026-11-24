using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class PolicyChainTests
{
    private static PolicyRef On(int id) => new(id, true);
    private static PolicyRef Off(int id) => new(id, false);

    [Fact]
    public void MostSpecificLevelWins()
    {
        Assert.Equal(1, PolicyChain.Pick(false, On(1), On(2), On(3)));
        Assert.Equal(2, PolicyChain.Pick(false, null, On(2), On(3)));
        Assert.Equal(3, PolicyChain.Pick(false, null, null, On(3)));
    }

    [Fact]
    public void NothingAssignedMeansNoPolicy()
    {
        Assert.Null(PolicyChain.Pick(false, null, null, null));
        Assert.Null(PolicyChain.Source(false, null, null, null));
    }

    [Fact]
    public void InactivePolicyIsSkippedSoTheNextLevelStillApplies()
    {
        Assert.Equal(2, PolicyChain.Pick(false, Off(1), On(2), On(3)));
        Assert.Equal(3, PolicyChain.Pick(false, Off(1), Off(2), On(3)));
        Assert.Null(PolicyChain.Pick(false, Off(1), Off(2), Off(3)));
    }

    [Fact]
    public void BlockInheritanceSwitchesOffEveryLevel()
    {
        Assert.Null(PolicyChain.Pick(true, On(1), On(2), On(3)));
        Assert.Null(PolicyChain.Source(true, On(1), On(2), On(3)));
    }

    [Fact]
    public void SourceNamesTheLevelThePolicyComesFrom()
    {
        Assert.Equal("agent", PolicyChain.Source(false, On(1), On(2), On(3)));
        Assert.Equal("site", PolicyChain.Source(false, Off(1), On(2), On(3)));
        Assert.Equal("client", PolicyChain.Source(false, null, null, On(3)));
    }

    [Fact]
    public void SamePolicyOnSeveralLevelsStillResolvesToThatPolicy()
    {
        Assert.Equal(7, PolicyChain.Pick(false, null, On(7), On(7)));
    }
}

public class PolicyChainGroupTests
{
    private static PolicyRef On(int id) => new(id, true);
    private static PolicyRef Off(int id) => new(id, false);

    [Fact]
    public void TheGroupSitsBetweenTheAgentAndTheSite()
    {
        Assert.Equal(1, PolicyChain.Pick(false, On(1), On(2), On(3), On(4)));
        Assert.Equal(2, PolicyChain.Pick(false, null, On(2), On(3), On(4)));
        Assert.Equal(3, PolicyChain.Pick(false, null, null, On(3), On(4)));
        Assert.Equal(4, PolicyChain.Pick(false, null, null, null, On(4)));
        Assert.Equal("group", PolicyChain.Source(false, null, On(2), On(3), On(4)));
    }

    [Fact]
    public void AnInactiveGroupPolicyIsSkippedAndBlockingStillWins()
    {
        Assert.Equal(3, PolicyChain.Pick(false, null, Off(2), On(3), On(4)));
        Assert.Null(PolicyChain.Pick(true, On(1), On(2), On(3), On(4)));
        Assert.Null(PolicyChain.Source(true, null, On(2), null, null));
    }
}
