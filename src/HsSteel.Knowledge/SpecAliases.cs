using System.Text;
using System.Text.RegularExpressions;

namespace HsSteel.Knowledge;

public sealed record AliasRow(string Alias, string Lang, int Priority);

/// <summary>Spec normalization and Korean/English alias generation for sections and bolts.</summary>
public static class SpecAliases
{
    private static readonly Dictionary<string, string[]> KoreanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["H-BEAM"] = ["H형강", "에이치빔", "H빔"],
        ["BH-BEAM"] = ["BH빔", "조립H형강", "빌트업H"],
        ["LH-BEAM"] = ["경량H형강", "LH빔"],
        ["PEB-BEAM"] = ["PEB빔", "변단면보"],
        ["I-BEAM"] = ["I형강", "아이빔"],
        ["ANGLE"] = ["앵글", "ㄱ형강", "등변ㄱ형강"],
        ["CHANNEL"] = ["채널", "ㄷ형강"],
        ["C-CHANNEL"] = ["C형강", "립채널", "씨찬넬"],
        ["T-BAR"] = ["T형강", "티바"],
        ["Z-BAR"] = ["Z형강", "제트바"],
        ["FLAT-BAR"] = ["평철", "플랫바"],
        ["ROUND-BAR"] = ["환봉", "라운드바"],
        ["STEEL-PIPE"] = ["강관", "파이프", "원형강관"],
        ["SQ-PIPE"] = ["각관", "각파이프", "각형강관"],
        ["PLATE"] = ["철판", "플레이트"],
        ["USERSHAPE"] = ["사용자형상"],
        ["DECK-PLATE"] = ["데크플레이트", "데크"],
        ["GRATING"] = ["그레이팅"],
        ["CONCRETE"] = ["콘크리트", "RC기둥"],
    };

    private static readonly Regex Spaces = new(@"[\s\-_]+", RegexOptions.CultureInvariant);

    /// <summary>Canonical comparison form: upper-case, diameter marks to Φ, '×'/'*' to X, no spaces/hyphens/underscores.</summary>
    public static string Normalize(string s)
    {
        var t = s.Trim().ToUpperInvariant()
            .Replace("%%C", "Φ", StringComparison.Ordinal)
            .Replace('Ø', 'Φ').Replace('∅', 'Φ').Replace('φ', 'Φ')
            .Replace('×', 'X').Replace('*', 'X');
        return Spaces.Replace(t, string.Empty).Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<string> KoreanFor(string family) =>
        KoreanNames.TryGetValue(family, out var k) ? k : [];

    /// <summary>Aliases for a section spec such as "H400x200x8x13" (family "H-BEAM").</summary>
    public static IReadOnlyList<AliasRow> ForSection(string spec, string family)
    {
        var rows = new List<AliasRow> { new(spec, "en", 0) };
        var i = 0;
        while (i < spec.Length && !char.IsDigit(spec[i]))
        {
            i++;
        }

        if (i < spec.Length)
        {
            var prefix = spec[..i].TrimEnd('-', '_');
            var body = spec[i..];
            var parts = body.Split('x', 'X', '*');
            if (prefix.Length > 0)
            {
                rows.Add(new(prefix + "-" + body, "en", 1));
                if (parts.Length >= 3)
                {
                    rows.Add(new(prefix + parts[0] + "x" + parts[1], "en", 2));
                    rows.Add(new(prefix + "-" + parts[0] + "x" + parts[1], "en", 2));
                }

                if (parts.Length >= 2)
                {
                    rows.Add(new(prefix + parts[0], "en", 3));
                    rows.Add(new(prefix + "-" + parts[0], "en", 3));
                }
            }

            foreach (var k in KoreanFor(family))
            {
                rows.Add(new(k + " " + body, "ko", 1));
                if (parts.Length >= 3)
                {
                    rows.Add(new(k + " " + parts[0] + "x" + parts[1], "ko", 2));
                }

                if (parts.Length >= 2)
                {
                    rows.Add(new(k + " " + parts[0], "ko", 3));
                }
            }
        }

        return Dedup(rows);
    }

    private static readonly Regex BoltRx = new(@"^(?<grade>[A-Z0-9]+)\s*M(?<d>\d+)\s*[*xX×]\s*(?<l>\d+)$", RegexOptions.CultureInvariant);

    public static bool TryParseBolt(string spec, out string grade, out int diameter, out int length)
    {
        var m = BoltRx.Match(spec.Trim().ToUpperInvariant());
        grade = m.Success ? m.Groups["grade"].Value : string.Empty;
        diameter = m.Success ? int.Parse(m.Groups["d"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        length = m.Success ? int.Parse(m.Groups["l"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        return m.Success;
    }

    /// <summary>Aliases for a high-tension bolt spec such as "TS M20*60" (torque-shear HTB).</summary>
    public static IReadOnlyList<AliasRow> ForBolt(string spec)
    {
        if (!TryParseBolt(spec, out var grade, out var d, out var l))
        {
            return [new(spec, "en", 0)];
        }

        var rows = new List<AliasRow>
        {
            new(spec, "en", 0),
            new($"{grade} M{d}x{l}", "en", 0),
            new($"M{d}x{l}", "en", 1),
            new($"HTB M{d}x{l}", "en", 1),
            new($"{grade} M{d}", "en", 2),
            new($"HTB M{d}", "en", 2),
            new($"M{d}", "en", 4),
            new($"고장력볼트 M{d}x{l}", "ko", 1),
            new($"고장력볼트 M{d}", "ko", 2),
            new($"TS볼트 M{d}", "ko", 2),
            new($"볼트 M{d}", "ko", 3),
        };
        return Dedup(rows);
    }

    private static List<AliasRow> Dedup(List<AliasRow> rows) =>
        rows.GroupBy(r => Normalize(r.Alias), StringComparer.Ordinal)
            .Select(g => g.OrderBy(r => r.Priority).ThenBy(r => r.Alias, StringComparer.Ordinal).First())
            .OrderBy(r => r.Priority).ThenBy(r => r.Alias, StringComparer.Ordinal)
            .ToList();
}
