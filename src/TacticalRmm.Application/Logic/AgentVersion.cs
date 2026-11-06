namespace TacticalRmm.Application.Logic;

public static class AgentVersion
{
    private static Version? Parse(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var core = v.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out var r) ? r : null;
    }

    /// <summary>Installer lama ditolak, kecuali versi terbaru berakhiran "-dev" (perilaku Django).</summary>
    public static bool IsInstallerOutdated(string installerVersion, string latest)
    {
        if (latest.Contains("-dev")) return false;
        var a = Parse(installerVersion); var b = Parse(latest);
        return a is null || (b is not null && a < b);
    }
}
