namespace TacticalRmm.Application.Logic;

public static class PatchPolicyResolver
{
    /// <summary>Nilai efektif reboot_after_install: setting agent menang kecuali "inherit".</summary>
    public static string EffectiveReboot(string agentValue, string? policyValue) =>
        agentValue != "inherit" ? agentValue : (policyValue ?? agentValue);

    public static bool ShouldReboot(string effective, bool needsReboot) =>
        effective == "always" || (needsReboot && effective == "required");
}
