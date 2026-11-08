using System.Text.RegularExpressions;

namespace TacticalRmm.Application.Logic;

/// <summary>Port Agent.delete_superseded_updates: for duplicate KBs, keep only the newest version based on "(Version x)" in the title.</summary>
public static partial class SupersededUpdates
{
    [GeneratedRegex(@"\((Version|Versão)(.*?)\)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();

    public static List<long> FindObsolete(IEnumerable<(long Id, string? Kb, string? Title)> updates)
    {
        var obsolete = new List<long>();
        foreach (var group in updates.Where(u => u.Kb != null).GroupBy(u => u.Kb!).Where(g => g.Count() > 1))
        {
            var parsed = new List<(long Id, string Ver)>();
            var ok = true;
            foreach (var u in group)
            {
                var m = VersionPattern().Match(u.Title ?? "");
                if (!m.Success) { ok = false; break; }   // no version info: skip this KB (same as Django)
                parsed.Add((u.Id, m.Groups[2].Value.Trim()));
            }
            if (!ok) continue;
            parsed.Sort((a, b) => CompareLoose(a.Ver, b.Ver));
            obsolete.AddRange(parsed.Take(parsed.Count - 1).Select(p => p.Id));
        }
        return obsolete;
    }

    // Simple equivalent of distutils.LooseVersion.
    public static int CompareLoose(string a, string b)
    {
        var pa = Regex.Split(a, @"[.\-_ ]+"); var pb = Regex.Split(b, @"[.\-_ ]+");
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            if (i >= pa.Length) return -1;
            if (i >= pb.Length) return 1;
            var c = long.TryParse(pa[i], out var x) && long.TryParse(pb[i], out var y)
                ? x.CompareTo(y) : string.CompareOrdinal(pa[i], pb[i]);
            if (c != 0) return c;
        }
        return 0;
    }
}
