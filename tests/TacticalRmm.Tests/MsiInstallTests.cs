using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class MsiInstallTests
{
    [Theory]
    [InlineData("https://dl.example.com/app/setup.msi", true)] [InlineData("https://dl.example.com/setup.MSI?token=1&x=2", true)] [InlineData(@"\\fs01\apps\setup.msi", true)]
    [InlineData("http://dl.example.com/setup.msi", false)] [InlineData("https://dl.example.com/setup.exe", false)] [InlineData(@"C:\setup.msi", false)] [InlineData(@"\\fs01\setup.msi", false)]
    [InlineData("", false)] [InlineData("https://x.com/a b.msi", false)] [InlineData("https://x.com/a.msi';calc;'", false)] [InlineData("ftp://x.com/a.msi", false)]
    public void OnlyHttpsOrSharePathsEndingInMsi(string source, bool ok) => Assert.Equal(ok, MsiInstall.CheckSource(source) is null);

    [Theory]
    [InlineData("/qn /norestart", true)] [InlineData("/qn INSTALLDIR=\"C:\\Program Files\\App\" ALLUSERS=1", true)] [InlineData("", true)]
    [InlineData("/qn; calc", false)] [InlineData("/qn & calc", false)] [InlineData("/qn `x`", false)] [InlineData("/qn $x", false)] [InlineData("/qn 'x'", false)]
    public void InstallOptionsAreKeptSimple(string args, bool ok) => Assert.Equal(ok, MsiInstall.CheckArgs(args) is null);

    [Fact] public void ShaAndTimeoutAreChecked()
    {
        Assert.Null(MsiInstall.CheckSha(null)); Assert.Null(MsiInstall.CheckSha(new string('a', 64))); Assert.NotNull(MsiInstall.CheckSha("abc"));
        Assert.NotNull(MsiInstall.Validate("https://x.com/a.msi", null, null, 10));
        Assert.NotNull(MsiInstall.Validate("https://x.com/a.msi", null, null, 99999));
        Assert.Null(MsiInstall.Validate("https://x.com/a.msi", null, null, 900));
    }

    [Fact] public void TheScriptCarriesTheOptionsAndEscapesQuotes()
    {
        var s = MsiInstall.Script("https://x.com/it's.msi", new string('A', 64), null, true);
        Assert.Contains("'https://x.com/it''s.msi'", s);
        Assert.Contains("/qn /norestart REINSTALL=ALL REINSTALLMODE=vomus", s);
        Assert.Contains("Get-FileHash", s);
        Assert.DoesNotContain("REINSTALL=ALL", MsiInstall.Script("https://x.com/a.msi", null, "/qn", false));
        Assert.Contains("'/qn /norestart'", MsiInstall.Script("https://x.com/a.msi", null, "", false));   // empty options fall back to the silent default
    }

    [Fact] public void BulkRequiresTheSoftwarePermissionAndAValidRequest()
    {
        Assert.Equal(TacticalRmm.Domain.Perm.ManageSoftware, BulkRules.RequiredPermission("install_msi"));
        var ok = new BulkRequest("install_msi", ["agent-agent-agent-agent-1"], Timeout: 900, Url: "https://x.com/a.msi");
        Assert.Null(BulkRules.Validate(ok));
        Assert.NotNull(BulkRules.Validate(ok with { Url = "http://x.com/a.msi" }));
        Assert.NotNull(BulkRules.Validate(ok with { Timeout = 30 }));
    }

    [Theory]
    [InlineData("ok:Installed (exit code 0).", "ok")] [InlineData("fail:msiexec exit code 1603", "failed")] [InlineData("offline:The agent did not answer", "offline")]
    public void BulkReadsTheInstallReply(string reply, string status) => Assert.Equal(status, BulkRules.FromReply("install_msi", reply).Status);
}
