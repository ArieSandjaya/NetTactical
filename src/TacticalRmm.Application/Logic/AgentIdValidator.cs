using System.Text.RegularExpressions;

namespace TacticalRmm.Application.Logic;

public static partial class AgentIdValidator
{
    // agent_id dibuat agent (>= 20 karakter, tanpa '/'), sama dengan converter URL Django: [^/]{20}[^/]+
    [GeneratedRegex(@"^[^/\s]{20}[^/\s]+$")]
    private static partial Regex Pattern();
    public static bool IsValid(string? id) => !string.IsNullOrEmpty(id) && Pattern().IsMatch(id);
}
