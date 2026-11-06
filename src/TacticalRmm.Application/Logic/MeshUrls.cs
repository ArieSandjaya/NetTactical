using System.Web;
using TacticalRmm.Domain;

namespace TacticalRmm.Application.Logic;

public static class MeshAgentIdent
{
    public const int Win32 = 3, Win64 = 4, Linux32 = 5, Linux64 = 6, LinuxArm64 = 26, LinuxArmHf = 25, DarwinUniversal = 10005;

    /// <summary>Pemetaan MeshExe (installer). null = arsitektur tidak didukung.</summary>
    public static int? ForInstaller(string? plat, string? goarch) => (plat, goarch) switch
    {
        (AgentPlat.Windows, GoArch.Amd64) => Win64,
        (AgentPlat.Windows, GoArch.I386) => Win32,
        (AgentPlat.Darwin, GoArch.Amd64) or (AgentPlat.Darwin, GoArch.Arm64) => DarwinUniversal,
        _ => null
    };

    /// <summary>Pemetaan MeshReinstall (hanya Windows di Django).</summary>
    public static int ForReinstall(string? goarch) => goarch == GoArch.Amd64 ? Win64 : Win32;
}

public static class MeshUrls
{
    /// <param name="baseUrl">http://127.0.0.1:4430 (lokal) atau mesh_site (eksternal), tanpa slash akhir.</param>
    public static string AgentDownload(string baseUrl, int ident, string plat, string meshDeviceId)
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        if (plat == AgentPlat.Windows)
        {
            q["id"] = ident.ToString(); q["meshid"] = meshDeviceId; q["installflags"] = "0";
        }
        else
        {
            q["id"] = meshDeviceId; q["installflags"] = "2"; q["meshinstall"] = ident.ToString();
        }
        return baseUrl.TrimEnd('/') + "/meshagents?" + q;
    }

    public static string ControlSocket(string site, bool useExternal, int localPort, string authToken) =>
        useExternal
            ? $"{site.TrimEnd('/').Replace("https", "wss")}/control.ashx?auth={authToken}"
            : $"ws://127.0.0.1:{localPort}/control.ashx?auth={authToken}";
}
