using System.Globalization;

namespace TacticalRmm.Application.Logic;

/// <summary>Checks and normalises custom field definitions and values. Pure logic, no database.</summary>
public static class CustomFieldRules
{
    public static readonly string[] Models = ["client", "site", "agent"];
    public static readonly string[] Types = ["text", "number", "checkbox", "datetime", "single"];
    public const int MaxText = 2000, MaxOptions = 50, MaxOptionLength = 100;

    /// <summary>The choices of a "single" field: one per line, trimmed, no blanks, no duplicates.</summary>
    public static List<string> ParseOptions(string? options) =>
        (options ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Null when the definition is fine, otherwise what is wrong with it.</summary>
    public static string? CheckDefinition(string? name, string? model, string? type, string? options, string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return "name is required (max 100 characters)";
        if (model is null || !Models.Contains(model)) return "model must be client, site or agent";
        if (type is null || !Types.Contains(type)) return "type must be text, number, checkbox, datetime or single";
        if (type == "single")
        {
            var list = ParseOptions(options);
            if (list.Count == 0) return "a single-choice field needs at least one option";
            if (list.Count > MaxOptions) return $"at most {MaxOptions} options";
            if (list.Any(o => o.Length > MaxOptionLength)) return $"an option is longer than {MaxOptionLength} characters";
        }
        if (!string.IsNullOrWhiteSpace(defaultValue))
        {
            var (ok, _, err) = Normalize(type, options, defaultValue);
            if (!ok) return "default value: " + err;
        }
        return null;
    }

    /// <summary>
    /// Validates a value for a field type. Returns the form that is stored: "" means "no value" (blank input),
    /// numbers use a dot, checkboxes are "true"/"false", dates are "yyyy-MM-ddTHH:mm".
    /// </summary>
    public static (bool Ok, string Value, string? Error) Normalize(string type, string? options, string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return (true, "", null);
        switch (type)
        {
            case "text":
                return v.Length > MaxText ? (false, "", $"text is longer than {MaxText} characters") : (true, v, null);
            case "number":
                return decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                    ? (true, n.ToString(CultureInfo.InvariantCulture), null) : (false, "", "not a number");
            case "checkbox":
                if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) return (true, "true", null);
                if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) return (true, "false", null);
                return (false, "", "must be true or false");
            case "datetime":
                return DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? (true, d.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture), null) : (false, "", "not a date and time");
            case "single":
                return ParseOptions(options).Contains(v) ? (true, v, null) : (false, "", "not one of the options");
            default:
                return (false, "", "unknown field type");
        }
    }
}
