using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class AppInstallTests
{
    private static SoftwarePackageRequest Exe() => new() { Name = "Adobe Acrobat Reader", InstallerType = "exe", Source = "https://files.example.com/AcroRdrDC.exe", SilentArgs = "/sAll /rs /msi EULA_ACCEPT=YES", TimeoutSeconds = 1200, DetectName = "Adobe Acrobat" };

    [Fact] public void AnAcrobatStylePackageIsAccepted() => Assert.Null(AppInstall.Validate(Exe()));

    [Fact] public void AnMsiNeedsNoOptionsButAnExeDoes()
    {
        Assert.Null(AppInstall.Validate(new SoftwarePackageRequest { Name = "7-Zip", InstallerType = "msi", Source = "https://files.example.com/7z.msi" }));
        var r = Exe(); r.SilentArgs = "";
        Assert.Contains("silent", AppInstall.Validate(r)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void TheAddressMustMatchTheTypeAndBeSafe()
    {
        var r = Exe();
        r.Source = "https://files.example.com/app.msi"; Assert.NotNull(AppInstall.Validate(r));            // wrong extension for exe
        r.Source = "http://files.example.com/app.exe"; Assert.NotNull(AppInstall.Validate(r));            // not https
        r.Source = @"\\srv\share\app.exe"; Assert.Null(AppInstall.Validate(r));                           // network share
        r.Source = "https://x.example.com/a.exe;calc"; Assert.NotNull(AppInstall.Validate(r));
        r.Source = "https://x.example.com/a b.exe"; Assert.NotNull(AppInstall.Validate(r));
    }

    [Fact] public void OtherFieldsAreChecked()
    {
        var r = Exe(); r.Name = " "; Assert.NotNull(AppInstall.Validate(r));
        r = Exe(); r.InstallerType = "zip"; Assert.NotNull(AppInstall.Validate(r));
        r = Exe(); r.Sha256 = "abc"; Assert.NotNull(AppInstall.Validate(r));
        r = Exe(); r.Sha256 = new string('A', 64); Assert.Null(AppInstall.Validate(r));
        r = Exe(); r.SilentArgs = "/S; calc"; Assert.NotNull(AppInstall.Validate(r));
        r = Exe(); r.TimeoutSeconds = 10; Assert.NotNull(AppInstall.Validate(r));
        r = Exe(); r.DetectName = "a'b"; Assert.NotNull(AppInstall.Validate(r));
        r = Exe(); r.Note = "a\nb"; Assert.NotNull(AppInstall.Validate(r));
    }

    [Fact] public void ADeploymentNeedsAPackageATargetAndAKnownExpiry()
    {
        Assert.Null(AppInstall.Validate(new AppDeploymentRequest { PackageId = 1, SiteIds = [3] }));
        Assert.NotNull(AppInstall.Validate(new AppDeploymentRequest { PackageId = 0, SiteIds = [3] }));
        Assert.NotNull(AppInstall.Validate(new AppDeploymentRequest { PackageId = 1 }));
        Assert.NotNull(AppInstall.Validate(new AppDeploymentRequest { PackageId = 1, SiteIds = [3], ExpiresDays = 5 }));
        Assert.NotNull(AppInstall.Validate(new AppDeploymentRequest { PackageId = 1, AgentIds = ["bad id"] }));
    }

    [Fact] public void TheExeScriptRunsTheInstallerWithItsOptionsAndSkipsWhenAlreadyInstalled()
    {
        var s = AppInstall.Script("exe", "https://files.example.com/AcroRdrDC.exe", "ABCD", "/sAll /rs", "Adobe Acrobat", reinstall: false);
        Assert.Contains("'https://files.example.com/AcroRdrDC.exe'", s);
        Assert.Contains("$extra = '/sAll /rs'", s);
        Assert.Contains("$detect = 'Adobe Acrobat'", s);
        Assert.Contains("Get-FileHash", s);
        Assert.Contains("Start-Process -FilePath $tmp", s);
        Assert.Contains(AppInstall.AlreadyInstalled, s);
    }

    [Fact] public void ReinstallIgnoresTheCheckAndAddsRepairOptionsForAnMsi()
    {
        var s = AppInstall.Script("msi", "https://files.example.com/a.msi", null, null, "App", reinstall: true);
        Assert.Contains("$detect = ''", s);
        Assert.Contains("/qn /norestart REINSTALL=ALL REINSTALLMODE=vomus", s);
    }

    [Fact] public void QuotesInValuesCannotBreakOutOfTheScript()
    {
        var s = AppInstall.Script("exe", "https://x.example.com/a.exe", null, "/S", "O'Brien", reinstall: false);
        Assert.Contains("$detect = 'O''Brien'", s);
    }

    [Fact] public void AnswersBecomeStatuses()
    {
        Assert.Equal((AppInstall.Ok, "Installed (exit code 0)."), AppInstall.Interpret("Installed (exit code 0).", null, 1));
        Assert.Equal(AppInstall.Skipped, AppInstall.Interpret("Already installed: Adobe Acrobat 25.1. Skipped.", null, 1).Status);
        Assert.Equal(AppInstall.Failed, AppInstall.Interpret(null, "Installer exit code 1603", 1).Status);
        Assert.Equal(AppInstall.Pending, AppInstall.Interpret(null, "The computer did not answer in time", 1).Status);                 // offline: keep waiting
        Assert.Equal(AppInstall.Failed, AppInstall.Interpret(null, "The computer did not answer in time", AppInstall.MaxAttempts).Status);   // too many tries
        Assert.Equal(AppInstall.Ok, AppInstall.Interpret("", null, 1).Status);
    }
}
