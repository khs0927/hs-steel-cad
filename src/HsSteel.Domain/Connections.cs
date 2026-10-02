using System.Globalization;
using System.Text.RegularExpressions;

namespace HsSteel.Domain;

/// <summary>
/// One axis of an HS-STEEL bolt string, e.g. "40+2A60+40" = edge 40, 2 intervals of 60, edge 40.
/// Holes sit at the start and every interval end of the "nAp" group; the plain terms are edges/gaps.
/// "40+0A0+40" is a single hole line. An axis without a group (e.g. flange "120+0+37.5+35") has no holes here.
/// </summary>
public sealed record BoltAxis(IReadOnlyList<double> Before, int Intervals, double Pitch, IReadOnlyList<double> After, bool HasGroup)
{
    public double Total => Before.Sum() + (Intervals * Pitch) + After.Sum();

    public double FirstHole => Before.Sum();

    public IEnumerable<double> Holes => HasGroup ? Enumerable.Range(0, Intervals + 1).Select(i => FirstHole + (i * Pitch)) : [];

    public int Count => HasGroup ? Intervals + 1 : 0;

    public static BoltAxis Parse(string s)
    {
        var before = new List<double>();
        var after = new List<double>();
        var (n, p, group) = (0, 0.0, false);
        foreach (var term in s.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var a = term.IndexOf('A', StringComparison.OrdinalIgnoreCase);
            if (a > 0 && !group)
            {
                n = int.Parse(term[..a], CultureInfo.InvariantCulture);
                p = double.Parse(term[(a + 1)..], CultureInfo.InvariantCulture);
                group = true;
            }
            else
            {
                (group ? after : before).Add(double.Parse(term, CultureInfo.InvariantCulture));
            }
        }

        return new BoltAxis(before, n, p, after, group);
    }

    public string Format()
    {
        var parts = Before.Select(F).ToList();
        if (HasGroup)
        {
            parts.Add($"{Intervals}A{F(Pitch)}");
        }

        parts.AddRange(After.Select(F));
        return string.Join('+', parts);
    }

    internal static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// HS-STEEL plate/bolt data string, e.g. "9TX40+2A30+40Y40+3A60+40D20" (9 mm plate, M20, 3x4 holes) or
/// "9T12TD20X40+2A60+40Y120+0+37.5+35" (flange splice: inner 9, outer 12). Grammar:
/// thicknesses ("&lt;t&gt;T")*, then in any order X&lt;axis&gt;, Y&lt;axis&gt;, D&lt;bolt dia&gt;.
/// </summary>
public sealed record BoltPattern(IReadOnlyList<double> Thicknesses, BoltAxis? X, BoltAxis? Y, double BoltDia)
{
    private static readonly Regex Thk = new(@"^(\d+(?:\.\d+)?)T", RegexOptions.Compiled);
    private static readonly Regex Part = new(@"([XYD])([0-9.A+\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public double HoleDia => BoltDia > 0 ? BoltDia + 2 : 0;

    public static BoltPattern Parse(string s)
    {
        var rest = s.Trim();
        var t = new List<double>();
        for (var m = Thk.Match(rest); m.Success; m = Thk.Match(rest))
        {
            t.Add(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            rest = rest[m.Length..];
        }

        BoltAxis? x = null, y = null;
        double d = 0;
        foreach (Match m in Part.Matches(rest))
        {
            var v = m.Groups[2].Value.TrimEnd('+');
            switch (char.ToUpperInvariant(m.Groups[1].Value[0]))
            {
                case 'X': x = BoltAxis.Parse(v); break;
                case 'Y': y = BoltAxis.Parse(v); break;
                default: d = double.Parse(v, CultureInfo.InvariantCulture); break;
            }
        }

        return new BoltPattern(t, x, y, d);
    }

    public string Format()
    {
        var s = string.Concat(Thicknesses.Select(v => BoltAxis.F(v) + "T"));
        if (X is not null)
        {
            s += "X" + X.Format();
        }

        if (Y is not null)
        {
            s += "Y" + Y.Format();
        }

        return BoltDia > 0 ? s + "D" + BoltAxis.F(BoltDia) : s;
    }
}

/// <summary>
/// One row of the HS-STEEL standard splice tables (SCSS-{C|G}{16|20|22}.dat): column (C) or girder (G)
/// splice of an H section with M16/M20/M22 high-strength bolts.
///
/// Column meaning — decoded from the HS-STEEL manual's block examples
/// ("9TX40+2A30+40Y40+3A60+40D20" web, "9T12TD20X40+2A60+40Y120+0+37.5+35" flange, SPGAP 5)
/// and checked against every row of the 6 tables (175 rows).
///   S1 web plate t · S2+S3AS4+S5 web X (along member, per side) · S6+S7AS8+S9 web Y (depth) · S10 web bolt dia
///   S11 flange inner plate t · S12 flange outer plate t · S13 flange bolt dia
///   S14+S15AS16+S17 flange X (per side) · S18 flange gauge (inner pair) · S19 second gauge offset · S20,S21 flange edges (kept)
///   S22 splice gap · S23 flange bolt total (lines across = S23 / (4 x rows): 2, or 4 with the outer pair S19 further out)
///   S24 flange bolt name · S25 web bolt count as tabulated (agrees with 2 x rows x cols on 148/175 rows; not used)
///   S26 web bolt name · S27 material.
/// </summary>
public sealed record SpliceSpec(string Section, string Standard, IReadOnlyList<string> Raw)
{
    private double N(int i) => double.Parse(Raw[i - 1], CultureInfo.InvariantCulture);

    public double WebPlateT => N(1);

    public BoltAxis WebX => BoltAxis.Parse($"{Raw[1]}+{Raw[2]}A{Raw[3]}+{Raw[4]}");

    public BoltAxis WebY => BoltAxis.Parse($"{Raw[5]}+{Raw[6]}A{Raw[7]}+{Raw[8]}");

    public double WebBoltDia => N(10);

    public double FlangeInnerT => N(11);

    public double FlangeOuterT => N(12);

    public double FlangeBoltDia => N(13);

    public BoltAxis FlangeX => BoltAxis.Parse($"{Raw[13]}+{Raw[14]}A{Raw[15]}+{Raw[16]}");

    public double FlangeGauge => N(18);

    public double Gap => N(22);

    public string FlangeBolt => Raw[23];

    /// <summary>Second flange gauge offset (S19): with four bolt lines the outer pair sits S19 outside the inner pair.</summary>
    public double FlangeGauge2 => N(19);

    /// <summary>Flange bolt total of the joint (S23) — confirmed on every row: divisible by 4 x rows.</summary>
    public int FlangeBoltTotal => (int)N(23);

    /// <summary>Bolt lines across one flange: S23 / (2 flanges x 2 sides x rows) = 2 or 4.</summary>
    public int FlangeLines => FlangeX.Count > 0 ? Math.Max(2, FlangeBoltTotal / (4 * FlangeX.Count)) : 2;

    /// <summary>Bolt line positions across the flange, measured from the flange edge.</summary>
    public IReadOnlyList<double> FlangeLineOffsets(double flangeWidth)
    {
        var c = flangeWidth / 2;
        var g = FlangeGauge / 2;
        return FlangeLines >= 4 ? [c - g - FlangeGauge2, c - g, c + g, c + g + FlangeGauge2] : [c - g, c + g];
    }

    /// <summary>S25 as written in the table. Matches 2 x rows x columns on 148 of 175 legacy rows; geometry is used instead.</summary>
    public int WebBoltCountTable => (int)N(25);

    /// <summary>Web bolts of the joint from the layout: 2 sides x rows x columns.</summary>
    public int WebBoltCount => 2 * WebX.Count * WebY.Count;

    /// <summary>Flange bolts of the joint from the layout.</summary>
    public int FlangeBoltCount => 2 * 2 * FlangeX.Count * FlangeLines;

    public string WebBolt => Raw[25];

    public string Material => Raw[26];

    /// <summary>Web plate string in HS-STEEL notation (same as the legacy SPLICE WEB block).</summary>
    public string WebNotation => $"{BoltAxis.F(WebPlateT)}TX{WebX.Format()}Y{WebY.Format()}D{BoltAxis.F(WebBoltDia)}";

    /// <summary>Flange plate string in HS-STEEL notation.</summary>
    public string FlangeNotation =>
        $"{BoltAxis.F(FlangeInnerT)}T{BoltAxis.F(FlangeOuterT)}TD{BoltAxis.F(FlangeBoltDia)}X{FlangeX.Format()}Y{Raw[17]}+{Raw[18]}+{Raw[19]}+{Raw[20]}";

    public static IReadOnlyList<SpliceSpec> Load(string path)
    {
        var std = Path.GetFileNameWithoutExtension(path);
        var list = new List<SpliceSpec>();
        foreach (var line in File.ReadAllLines(path, Assets.SectionTable.Cp949).Skip(1))
        {
            var tokens = Regex.Matches(line, "\"[^\"]*\"|[^\\s()\"]+").Select(m => m.Value.Trim('"')).ToList();
            if (tokens.Count >= 28)
            {
                list.Add(new SpliceSpec(tokens[0], std, tokens.Skip(1).Take(27).ToList()));
            }
        }

        return list;
    }
}

/// <summary>All SCSS tables. Lookup by section, column/girder and bolt size.</summary>
public sealed class SpliceStandards
{
    private readonly Dictionary<string, SpliceSpec> _map = new(StringComparer.OrdinalIgnoreCase);

    public static SpliceStandards Empty { get; } = new();

    public int Count => _map.Count;

    public static SpliceStandards Load(string attributesDir)
    {
        var s = new SpliceStandards();
        if (Directory.Exists(attributesDir))
        {
            foreach (var f in Directory.GetFiles(attributesDir, "SCSS-*.dat"))
            {
                foreach (var row in SpliceSpec.Load(f))
                {
                    s._map[$"{row.Standard}|{row.Section}"] = row;
                }
            }
        }

        return s;
    }

    /// <summary>Finds a standard row; prefers the requested bolt size, then 22, 20, 16.</summary>
    public SpliceSpec? Find(string section, bool column, int boltSize = 0)
    {
        var kind = column ? "C" : "G";
        foreach (var size in new[] { boltSize, 22, 20, 16 }.Where(v => v > 0))
        {
            if (_map.TryGetValue($"SCSS-{kind}{size}|{section}", out var r))
            {
                return r;
            }
        }

        return null;
    }
}
