using System.Text.RegularExpressions;

namespace TacticalRmm.Application.Logic;

/// <summary>Rules and placeholder filling for URL actions. Pure, no database.</summary>
public static partial class UrlActions
{
    public static readonly IReadOnlyList<string> Placeholders =
    [
        "agent.hostname", "agent.agent_id", "agent.public_ip", "agent.logged_in_user", "agent.description", "client.name", "client.id", "site.name", "site.id",
    ];

    [GeneratedRegex(@"\{\{\s*([A-Za-z_.]+)\s*\}\}")]
    private static partial Regex Tag();

    public static string? Validate(string? name, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Give the action a name.";
        if (name.Trim().Length > 100) return "The name must be 100 characters or less.";
        if (string.IsNullOrWhiteSpace(pattern)) return "Enter the URL.";
        pattern = pattern.Trim();
        if (pattern.Length > 2000) return "The URL must be 2000 characters or less.";
        // The scheme has to be literal text, so a placeholder can never turn the link into something other than http(s).
        if (!pattern.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !pattern.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "The URL must start with http:// or https://.";
        if (Tag().Replace(pattern, "x").Any(char.IsWhiteSpace)) return "The URL must not contain spaces.";
        foreach (Match m in Tag().Matches(pattern))
            if (!Placeholders.Contains(m.Groups[1].Value.ToLowerInvariant())) return $"Unknown placeholder {{{{{m.Groups[1].Value}}}}}. Use one of: {string.Join(", ", Placeholders.Select(p => "{{" + p + "}}"))}.";
        return Uri.TryCreate(Tag().Replace(pattern, "x"), UriKind.Absolute, out _) ? null : "That does not look like a valid URL.";
    }

    /// <summary>Fills the placeholders with URL-encoded values; a missing value becomes empty.</summary>
    public static string Render(string pattern, Func<string, string?> value) =>
        Tag().Replace(pattern.Trim(), m => Uri.EscapeDataString(value(m.Groups[1].Value.ToLowerInvariant()) ?? ""));
}
