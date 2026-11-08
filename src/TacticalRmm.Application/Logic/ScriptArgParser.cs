using System.Text.RegularExpressions;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Logic;

/// <summary>Subset of Script.parse_script_args: replaces {{agent.xxx}}, {{site.name}}, {{client.name}}.</summary>
public static partial class ScriptArgParser
{
    [GeneratedRegex(@"\{\{\s*([a-zA-Z_]+)\.([a-zA-Z_]+)\s*\}\}")]
    private static partial Regex Token();

    public static List<string> Parse(Agent agent, string shell, IEnumerable<string>? args)
    {
        var result = new List<string>();
        foreach (var raw in args ?? Enumerable.Empty<string>())
        {
            var val = Token().Replace(raw, m => Resolve(agent, m.Groups[1].Value, m.Groups[2].Value) ?? m.Value);
            result.Add(val);
        }
        return result;
    }

    private static string? Resolve(Agent a, string obj, string field) => (obj, field) switch
    {
        ("agent", "hostname") => a.Hostname,
        ("agent", "agent_id") => a.AgentId,
        ("agent", "plat") => a.Plat,
        ("agent", "version") => a.Version,
        ("agent", "public_ip") => a.PublicIp,
        ("agent", "description") => a.Description,
        ("agent", "operating_system") => a.OperatingSystem,
        ("site", "name") => a.Site?.Name,
        ("client", "name") => a.Site?.Client?.Name,
        _ => null
    };
}
