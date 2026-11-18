using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class AgentUpdateTests
{
    [Fact]
    public void AnOldWindowsAgentGetsTheUrlVersionAndInstallerName()
    {
        var p = AgentUpdate.Decide("windows", "amd64", "2.8.0", "2.12.0");
        Assert.Null(p.Error); Assert.Null(p.Skip);
        Assert.Equal("agentupdate", p.Payload!["func"]);
        var pl = (IDictionary<string, object?>)p.Payload["payload"]!;
        Assert.Equal("https://github.com/amidaware/rmmagent/releases/download/v2.12.0/tacticalagent-v2.12.0-windows-amd64.exe", pl["url"]);
        Assert.Equal("2.12.0", pl["version"]);
        Assert.Equal("tacticalagent-v2.12.0-windows-amd64.exe", pl["inno"]);
    }

    [Fact] public void ACurrentAgentIsSkipped() => Assert.NotNull(AgentUpdate.Decide("windows", "amd64", "2.12.0", "2.12.0").Skip);
    [Fact] public void AnUnknownArchitectureDefaultsTo64Bit() => Assert.Contains("amd64", (string)((IDictionary<string, object?>)AgentUpdate.Decide("windows", null, "1.0.0", "2.12.0").Payload!["payload"]!)["url"]!);
    [Fact] public void LinuxIsRefused() => Assert.NotNull(AgentUpdate.Decide("linux", "amd64", "1.0.0", "2.12.0").Error);
    [Fact] public void ABadArchitectureOrVersionIsRefused()
    {
        Assert.NotNull(AgentUpdate.Decide("windows", "../x", "1.0.0", "2.12.0").Error);
        Assert.NotNull(AgentUpdate.Decide("windows", "amd64", "1.0.0", "latest").Error);
    }

    [Theory]
    [InlineData("started:Update started.", "ok")]
    [InlineData("skipped:Already on the latest version.", "ok")]
    [InlineData("offline:The agent did not answer.", "offline")]
    [InlineData("fail:NATS down", "failed")]
    public void BulkReadsTheUpdateReply(string reply, string status) => Assert.Equal(status, BulkRules.FromReply(BulkRules.AgentUpdateAction, reply).Status);

    [Fact] public void BulkNeedsTheManageAgentsPermission() => Assert.Equal(TacticalRmm.Domain.Perm.ManageAgents, BulkRules.RequiredPermission("agent_update"));
}
