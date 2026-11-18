using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class SoftwareUninstallTests
{
    private const string Guid = "{AC76BA86-7AD7-1033-7B44-AC0F074E4100}";

    [Theory]
    [InlineData("MsiExec.exe /I{AC76BA86-7AD7-1033-7B44-AC0F074E4100}")]
    [InlineData("MsiExec.exe /X{AC76BA86-7AD7-1033-7B44-AC0F074E4100}")]
    [InlineData("msiexec /x {AC76BA86-7AD7-1033-7B44-AC0F074E4100}")]
    [InlineData("\"C:\\Windows\\System32\\msiexec.exe\" /i{AC76BA86-7AD7-1033-7B44-AC0F074E4100}")]
    public void WindowsInstallerEntriesBecomeASilentUninstall(string registered)
    {
        var plan = SoftwareUninstall.Plan(registered)!;
        Assert.True(plan.Silent);
        Assert.Equal($"msiexec.exe /x {Guid} /qn /norestart", plan.Command);
    }

    [Fact]
    public void OtherUninstallersRunAsRegistered()
    {
        var plan = SoftwareUninstall.Plan("  \"C:\\Program Files\\Zoom\\uninstall.exe\" /S ")!;
        Assert.False(plan.Silent);
        Assert.Equal("\"C:\\Program Files\\Zoom\\uninstall.exe\" /S", plan.Command);
    }

    [Fact]
    public void AMsiStringWithExtraTextIsNotRewritten()
    {
        var plan = SoftwareUninstall.Plan("msiexec /x {AC76BA86-7AD7-1033-7B44-AC0F074E4100} /qb")!;
        Assert.False(plan.Silent);   // not the plain form: left exactly as registered
        Assert.Equal("msiexec /x {AC76BA86-7AD7-1033-7B44-AC0F074E4100} /qb", plan.Command);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a.exe\r\ncalc.exe")]
    [InlineData("a.exe\0")]
    public void EmptyOrMultiLineStringsGiveNoPlan(string? s) => Assert.Null(SoftwareUninstall.Plan(s));

    [Fact]
    public void OverlongStringsGiveNoPlan() => Assert.Null(SoftwareUninstall.Plan(new string('a', SoftwareList.MaxUninstall + 1)));

    [Fact]
    public void TheUninstallStringIsKeptThroughReplyAndStorage()
    {
        object reply = new List<object?> { new Dictionary<object, object?> { ["name"] = "Zoom", ["version"] = "5", ["uninstall"] = "MsiExec.exe /I" + Guid } };
        var items = SoftwareList.FromReply(reply)!;
        Assert.Equal("MsiExec.exe /I" + Guid, items[0].Uninstall);
        var again = SoftwareList.ParseJson(SoftwareList.ToJson(items));
        Assert.Equal(items[0].Uninstall, again[0].Uninstall);
    }
}
