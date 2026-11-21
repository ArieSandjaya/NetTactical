using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class DeploymentTests
{
    private static readonly DeploymentRequest Ok = new() { ClientId = 1, SiteId = 1, ExpiresHours = 168 };

    [Fact] public void ARequestIsValidatedLikeTheInstaller()
    {
        Assert.Null(DeploymentRules.Validate(Ok, "https://rmm.example.com"));
        Assert.NotNull(DeploymentRules.Validate(new() { ClientId = 1, SiteId = 1, AgentType = "laptop" }, "https://rmm.example.com"));
        Assert.NotNull(DeploymentRules.Validate(new() { ClientId = 1, SiteId = 1, Arch = "arm" }, "https://rmm.example.com"));
        Assert.NotNull(DeploymentRules.Validate(new() { ClientId = 1, SiteId = 1, ExpiresHours = 5 }, "https://rmm.example.com"));
        Assert.NotNull(DeploymentRules.Validate(new() { ClientId = 1, SiteId = 1, MaxUses = 0 }, "https://rmm.example.com"));
        Assert.NotNull(DeploymentRules.Validate(Ok, "https://rmm.example.com/x?y"));
        Assert.NotNull(DeploymentRules.Validate(new() { ClientId = 1, SiteId = 1, Note = "a\nb" }, "https://rmm.example.com"));
    }

    [Fact] public void UidsAreLongRandomAndChecked()
    {
        var a = DeploymentRules.NewUid(); var b = DeploymentRules.NewUid();
        Assert.NotEqual(a, b); Assert.True(DeploymentRules.ValidUid(a)); Assert.Equal(32, a.Length);
        Assert.False(DeploymentRules.ValidUid("short")); Assert.False(DeploymentRules.ValidUid(new string('G', 32))); Assert.False(DeploymentRules.ValidUid(null));
    }

    [Fact] public void LinkAndCommandPointAtTheServer()
    {
        var link = DeploymentRules.Link("https://rmm.example.com/", "abc");
        Assert.Equal("https://rmm.example.com/deploy/abc", link);
        Assert.Contains("irm 'https://rmm.example.com/deploy/abc' | iex", DeploymentRules.Command(link));
    }

    [Fact] public void TheScriptNeedsAdminSkipsInstalledComputersAndInstalls()
    {
        var s = DeploymentRules.Script("Acme\nCorp", "Main", "https://github.com/x/y.exe", "y.exe", "-m install");
        Assert.StartsWith("# NetTactical agent deployment: Acme Corp / Main\n", s);
        Assert.Contains("IsInRole", s); Assert.Contains("already installed", s); Assert.Contains("Invoke-WebRequest", s); Assert.Contains("-m install", s);
    }

    [Fact] public void ExpiryIsExclusive() { var n = DateTime.UtcNow; Assert.True(DeploymentRules.Expired(n, n)); Assert.False(DeploymentRules.Expired(n.AddSeconds(1), n)); }
}
