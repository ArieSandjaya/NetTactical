using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class InstallerCommandsTests
{
    private static readonly string Tok = new('a', 64);

    [Fact]
    public void DownloadUrlPointsToOfficialRelease()
    {
        Assert.True(InstallerCommands.DownloadUrl("2.12.0", "amd64") == "https://github.com/amidaware/rmmagent/releases/download/v2.12.0/tacticalagent-v2.12.0-windows-amd64.exe", "amd64 url");
        Assert.True(InstallerCommands.DownloadUrl("2.12.0", "386")!.EndsWith("windows-386.exe"), "386 url");
        Assert.True(InstallerCommands.DownloadUrl("2.12.0/../x", "amd64") is null, "bad version rejected");
        Assert.True(InstallerCommands.DownloadUrl("2.12.0", "arm") is null, "bad arch rejected");
    }

    [Fact]
    public void ApiValidationAcceptsOnlySchemeHostPort()
    {
        Assert.True(InstallerCommands.ValidateApi("https://rmm.example.com") is null, "https host");
        Assert.True(InstallerCommands.ValidateApi("http://10.0.0.5:8000") is null, "ip:port");
        foreach (var bad in new[] { "", "rmm.example.com", "https://a.com/path", "https://a.com\" & calc", "https://a b.com", "ftp://a.com", "https://a.com?x=1" })
            Assert.True(InstallerCommands.ValidateApi(bad) is not null, $"rejects '{bad}'");
        Assert.True(InstallerCommands.NormalizeApi(" https://a.com/ ") == "https://a.com", "normalize");
    }

    [Fact]
    public void InstallArgumentsMatchUpstreamAndOptionalFlags()
    {
        var a = InstallerCommands.InstallArguments("https://rmm.example.com", 3, 7, "server", Tok, false, false, false);
        Assert.True(a == $"-m install --api \"https://rmm.example.com\" --client-id 3 --site-id 7 --agent-type \"server\" --auth \"{Tok}\"", "base args");
        var b = InstallerCommands.InstallArguments("https://rmm.example.com", 3, 7, "workstation", Tok, true, true, true);
        Assert.True(b.EndsWith("--rdp --ping --power"), "flags");
        var c = InstallerCommands.InstallArguments("https://rmm.example.com", 3, 7, "workstation", Tok, false, false, false, noMesh: true);
        Assert.True(c.EndsWith($"--auth \"{Tok}\" --nomesh"), "nomesh");
        Assert.True(!a.Contains("--nomesh"), "mesh stays on when not asked");
    }

    [Fact]
    public void InstallArgumentsRejectInjection()
    {
        Assert.Throws<ArgumentException>(() => InstallerCommands.InstallArguments("https://a.com", 1, 1, "server\" & calc", Tok, false, false, false));
        Assert.Throws<ArgumentException>(() => InstallerCommands.InstallArguments("https://a.com", 1, 1, "server", Tok + "\" & calc", false, false, false));
        Assert.Throws<ArgumentException>(() => InstallerCommands.InstallArguments("https://a.com/x y", 1, 1, "server", Tok, false, false, false));
    }

    [Fact]
    public void CommandsHaveExpectedShape()
    {
        var args = InstallerCommands.InstallArguments("https://a.com", 1, 2, "server", Tok, false, false, false);
        var cmd = InstallerCommands.CommandPrompt("agent.exe", args);
        Assert.True(cmd.StartsWith("\"agent.exe\" /VERYSILENT /SUPPRESSMSGBOXES && ping 127.0.0.1 -n 5 && \"C:\\Program Files\\TacticalAgent\\tacticalrmm.exe\" -m install"), "cmd");
        var ps = InstallerCommands.PowerShell("https://example.com/agent.exe", "agent.exe", args);
        Assert.True(ps.Contains("Invoke-WebRequest -Uri 'https://example.com/agent.exe'") && ps.Contains("-m install"), "powershell");
    }
}
