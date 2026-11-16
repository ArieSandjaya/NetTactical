using TacticalRmm.Domain;

namespace TacticalRmm.Application.Logic;

/// <summary>An alert template that is assigned somewhere and whether it is switched on.</summary>
public readonly record struct TemplateRef(int Id, bool Enabled);

/// <summary>What a template asks for: always flag these kinds of alerts for notification.</summary>
public record AlertFlags(bool AgentOverdue, bool CheckFailures, bool TaskFailures);

/// <summary>
/// Which alert template applies to an agent, and what it changes. The most specific level wins:
/// site, then client, then the global default. A template that is switched off is skipped.
/// A template only ever adds a flag; it never removes the flag a check, task or agent already has.
/// </summary>
public static class AlertTemplateRules
{
    public static int? Pick(TemplateRef? site, TemplateRef? client, TemplateRef? global)
    {
        if (site is { Enabled: true }) return site.Value.Id;
        if (client is { Enabled: true }) return client.Value.Id;
        if (global is { Enabled: true }) return global.Value.Id;
        return null;
    }

    /// <summary>Whether an alert of this type counts as flagged: its own flag, or the template's.</summary>
    public static bool Flag(string alertType, bool itemFlag, AlertFlags? template)
    {
        if (itemFlag) return true;
        if (template is null) return false;
        return alertType switch
        {
            AlertType.Availability => template.AgentOverdue,
            AlertType.Check => template.CheckFailures,
            AlertType.Task => template.TaskFailures,
            _ => false,
        };
    }
}
