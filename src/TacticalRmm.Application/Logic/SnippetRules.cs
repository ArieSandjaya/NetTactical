using System.Text.RegularExpressions;

namespace TacticalRmm.Application.Logic;

/// <summary>Script snippets: names, finding {{name}} in a script, and putting the snippet code in. Pure logic.</summary>
public static partial class SnippetRules
{
    public const int MaxCode = 100_000, MaxExpanded = 1_000_000;

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_-]{1,64})\s*\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ValidName();

    public static string? CheckName(string? name) =>
        name is not null && ValidName().IsMatch(name.Trim()) ? null : "name must be 1-64 letters, digits, '-' or '_' (no spaces)";

    public static string? CheckCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? "code is required" : code.Length > MaxCode ? $"code is longer than {MaxCode} characters" : null;

    /// <summary>The distinct snippet names a script refers to.</summary>
    public static List<string> Names(string? body) =>
        string.IsNullOrEmpty(body) ? [] : Placeholder().Matches(body).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Replaces each {{name}} that has a snippet. One pass only: a snippet that itself contains {{other}} is not expanded again,
    /// so a snippet can never loop. Unknown names are left as they are. The result is cut off with an error when it grows too large.
    /// </summary>
    public static (string Body, string? Error) Expand(string? body, IReadOnlyDictionary<string, string> snippets)
    {
        if (string.IsNullOrEmpty(body) || snippets.Count == 0) return (body ?? "", null);
        var result = Placeholder().Replace(body, m => snippets.TryGetValue(m.Groups[1].Value, out var code) ? code : m.Value);
        return result.Length > MaxExpanded ? (body, $"the script is longer than {MaxExpanded} characters after snippets are inserted") : (result, null);
    }
}
