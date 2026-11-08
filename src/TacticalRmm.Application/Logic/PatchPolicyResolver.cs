namespace TacticalRmm.Application.Logic;

public static class PatchPolicyResolver
{
    /// <summary>Effective reboot_after_install value: the agent setting wins unless it is "inherit".</summary>
    public static string EffectiveReboot(string agentValue, string? policyValue) =>
        agentValue != "inherit" ? agentValue : (policyValue ?? agentValue);

    public static bool ShouldReboot(string effective, bool needsReboot) =>
        effective == "always" || (needsReboot && effective == "required");
}
