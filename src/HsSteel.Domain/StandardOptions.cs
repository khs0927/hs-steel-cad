using System.Globalization;

namespace HsSteel.Domain;

/// <summary>
/// Project-level standard options (docs/STANDARDS_RESEARCH.md): which bolt-length table, hole rule and mark scheme
/// a project follows. Values are plain strings so they round-trip through the project JSON; unknown values fall back
/// to the default. Everything here is a pure function of its inputs (deterministic).
/// </summary>
public static class StandardOptions
{
    public const string BoltKcs = "kcs";
    public const string BoltTsOneWasher = "ts_one_washer";
    public const string BoltByBoltSet = "by_bolt_set";
    public static readonly string[] BoltLengthTables = [BoltKcs, BoltTsOneWasher, BoltByBoltSet];
    /// <summary>
    /// Default = by bolt type: TS (S10T) sets use the one-washer table, hex HTB (F10T/F8T) sets the KCS table
    /// (KCS 14 31 25 table 2.1-5 and its note 2; legacy SCSS .dat TS 25/30/35 vs 단중.xlsx 2-washer set 30/35/40).
    /// See docs/DECISIONS_BOLT_MARKS.md. "kcs" / "ts_one_washer" force one table for every bolt.
    /// </summary>
    public const string DefaultBoltLengthTable = BoltByBoltSet;

    public const string HoleStandard = "standard";
    public const string HoleOversize = "oversize";
    public const string HoleLegacy = "legacy";
    public static readonly string[] HoleRules = [HoleStandard, HoleOversize, HoleLegacy];
    public const string DefaultHoleRule = HoleStandard;

    public const string MarkLegacy = "legacy";
    public const string MarkAlt = "alt";
    public static readonly string[] MarkSchemes = [MarkLegacy, MarkAlt];
    public const string DefaultMarkScheme = MarkLegacy;

    public const string FormatPlain = "plain";
    public const string FormatFloorPrefix = "floor_prefix";
    public static readonly string[] MarkFormats = [FormatPlain, FormatFloorPrefix];
    public const string DefaultMarkFormat = FormatPlain;

    private static string Norm(string? v, string[] allowed, string fallback)
    {
        var s = (v ?? "").Trim().ToLowerInvariant().Replace('-', '_');
        return allowed.Contains(s) ? s : fallback;
    }

    public static string NormalizeBoltLengthTable(string? v) => Norm(v, BoltLengthTables, DefaultBoltLengthTable);

    public static string NormalizeHoleRule(string? v) => Norm(v, HoleRules, DefaultHoleRule);

    public static string NormalizeMarkScheme(string? v) => Norm(v, MarkSchemes, DefaultMarkScheme);

    public static string NormalizeMarkFormat(string? v) => Norm(v, MarkFormats, DefaultMarkFormat);

    public static bool IsValid(string? v, string[] allowed) =>
        allowed.Contains((v ?? "").Trim().ToLowerInvariant().Replace('-', '_'));

    // ------------------------------------------------------------ bolt length (BL-001/002/003)

    /// <summary>KCS 14 31 25 table 2.1-5 (hex F10T, nut + 2 washers + 3 pitches): M16 30 ... M30 55.</summary>
    private static readonly (double Dia, double Add)[] Kcs = [(16, 30), (20, 35), (22, 40), (24, 45), (27, 50), (30, 55)];

    /// <summary>
    /// TS (torque-shear) set, 1 washer: the KCS value minus one washer (KCS 14 31 25 table 2.1-5 note 2) = JASS 6 / maker
    /// TC-bolt tables M16 25, M20 30, M22 35, M24 40, M27 45, M30 50; matches the legacy SCSS .dat bolts (TS M20 grip+30).
    /// </summary>
    private static readonly (double Dia, double Add)[] TsOneWasher = [(16, 25), (20, 30), (22, 35), (24, 40), (27, 45), (30, 50)];

    /// <summary>The concrete table ("kcs" or "ts_one_washer") used for a bolt grade under the project option.</summary>
    public static string ResolveBoltTable(string? option, string grade)
    {
        var o = NormalizeBoltLengthTable(option);
        return o == BoltByBoltSet ? (grade == Bolts.S10T ? BoltTsOneWasher : BoltKcs) : o;
    }

    /// <summary>Length added to the grip for a bolt of <paramref name="dia"/> (sizes outside the table use the nearest row).</summary>
    public static double AddLength(string? option, string grade, double dia)
    {
        var table = ResolveBoltTable(option, grade) == BoltTsOneWasher ? TsOneWasher : Kcs;
        var row = table.LastOrDefault(r => r.Dia <= dia + 1e-6);
        return row.Dia > 0 ? row.Add : table[0].Add;
    }

    /// <summary>Bolt length = ceil((grip + add) / 5) * 5 (BL-001).</summary>
    public static double BoltLength(string? option, string grade, double dia, double grip) =>
        Math.Ceiling(((grip + AddLength(option, grade, dia)) / 5) - 1e-9) * 5;

    /// <summary>Bolt label with length, e.g. "TS M20x75".</summary>
    public static string BoltLabel(string name, double length) =>
        length > 0 ? string.Create(CultureInfo.InvariantCulture, $"{name}x{length:0}") : name;

    // ------------------------------------------------------------ mark number width (NUM-002)

    /// <summary>
    /// Default assembly-mark number width: 3 ("C001"), as in the legacy M83 Numbering.dat HD-BOX templates
    /// (C001, G001, B001 ...). 1 gives the old unpadded engine marks ("C1"). See docs/DECISIONS_BOLT_MARKS.md.
    /// </summary>
    public const int DefaultMarkDigits = 3;

    public const int MaxMarkDigits = 6;

    /// <summary>Width clamped to 1..<see cref="MaxMarkDigits"/>; 0 or less means the default.</summary>
    public static int NormalizeMarkDigits(int digits) => digits <= 0 ? DefaultMarkDigits : Math.Min(digits, MaxMarkDigits);

    /// <summary>Assembly mark: head + running number zero-padded to <paramref name="digits"/> ("C" 7 3 → "C007"; 1 → "C7").</summary>
    public static string FormatMark(string head, int n, int digits) =>
        head + n.ToString(CultureInfo.InvariantCulture).PadLeft(NormalizeMarkDigits(digits), '0');

    // ------------------------------------------------------------ hole diameter (HL-001)

    /// <summary>
    /// Hole for a bolt: "standard" d+2 up to M22, d+3 from M24; "oversize" M16 20, M20 24, M22 28*, M24 30, M27 35*, M30 38
    /// (* inferred, not in the cited table); "legacy" d+2 for every size (what ModelBuilder and BoltPattern did before HL-001).
    /// </summary>
    public static double HoleFor(double dia, string? rule)
    {
        switch (NormalizeHoleRule(rule))
        {
            case HoleOversize:
                return dia switch
                {
                    <= 20 => dia + 4,
                    <= 24 => dia + 6,
                    _ => dia + 8,
                };
            case HoleLegacy:
                return dia + 2;
            default:
                return Bolts.HoleFor(dia);
        }
    }

    // ------------------------------------------------------------ marks (NUM-001)

    /// <summary>
    /// "alt" mark heads: C column, G girder/rafter/crane girder, B beam, V brace, PU purlin, GT girth, ST stair,
    /// HR handrail, T truss, AB anchor/embed, X other.
    /// (AB is how the legacy sheets name the anchor-bolt item, e.g. "AB M20(L-740)"; the legacy scheme uses EB.) (The brief's "J" joist has no engine assembly type.)
    /// </summary>
    public static string AltPrefix(AssemblyType t) => t switch
    {
        AssemblyType.Column or AssemblyType.SubColumn or AssemblyType.Post => "C",
        AssemblyType.Girder or AssemblyType.Rafter or AssemblyType.CraneGirder => "G",
        AssemblyType.Truss => "T",
        AssemblyType.Beam => "B",
        AssemblyType.Brace => "V",
        AssemblyType.Purlin => "PU",
        AssemblyType.Girth => "GT",
        AssemblyType.Stair => "ST",
        AssemblyType.HandRail => "HR",
        AssemblyType.Embed => "AB",
        _ => "X",
    };

    /// <summary>
    /// Floor tag for the "floor_prefix" format: the highest level at or below <paramref name="z"/> (with 1 mm tolerance);
    /// the leading digits of the level name when it has any ("2F" → "2"), otherwise its 1-based rank by elevation.
    /// Below every level, or with no levels, the tag is "1".
    /// </summary>
    public static string FloorTag(IReadOnlyList<(string Name, double Elevation)> levels, double z)
    {
        var sorted = levels.OrderBy(l => l.Elevation).ToList();
        var idx = -1;
        for (var i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].Elevation <= z + 1)
            {
                idx = i;
            }
        }

        if (idx < 0)
        {
            return "1";
        }

        var digits = new string(sorted[idx].Name.TakeWhile(char.IsAsciiDigit).ToArray());
        return digits.Length > 0 ? digits : (idx + 1).ToString(CultureInfo.InvariantCulture);
    }
}
