namespace HsSteel.Domain;

/// <summary>One row of the minimum fillet weld table: parts up to <paramref name="UpTo"/> mm thick need at least <paramref name="MinLeg"/>.</summary>
public sealed record FilletRule(double UpTo, double MinLeg);

/// <summary>
/// Fillet weld sizing from the minimum-size table common to KDS 14 31 25 / AWS D1.1 (Table 5.7, by the thicker
/// part joined): t ≤ 6 → 3, 6 &lt; t ≤ 12 → 5, 12 &lt; t ≤ 20 → 6, t &gt; 20 → 8. The leg never needs to exceed the
/// thinner part, so it is capped there.
/// </summary>
public static class WeldRules
{
    public static IReadOnlyList<FilletRule> MinFillet { get; } =
    [
        new(6, 3),
        new(12, 5),
        new(20, 6),
        new(double.MaxValue, 8),
    ];

    /// <summary>Minimum leg for the thicker part <paramref name="thicker"/> (table lookup).</summary>
    public static double MinFilletLeg(double thicker) => MinFillet.First(r => thicker <= r.UpTo + 1e-9).MinLeg;

    /// <summary>Fillet leg for a joint of two parts: table minimum by the thicker part, capped by the thinner part.</summary>
    public static double FilletLeg(double t1, double t2)
    {
        double thick = Math.Max(t1, t2), thin = Math.Min(t1, t2);
        var leg = MinFilletLeg(thick);
        return thin > 0 ? Math.Min(leg, thin) : leg;
    }
}

/// <summary>Bolt grades and hole sizes.</summary>
public static class Bolts
{
    /// <summary>High-strength friction bolt (KS B 1010 F10T).</summary>
    public const string F10T = "F10T";

    /// <summary>Torque-shear high-strength bolt (KS B 2819 S10T; HS-STEEL tables write "TS").</summary>
    public const string S10T = "S10T";

    /// <summary>Ordinary (4.6) machine bolt for secondary members.</summary>
    public const string MBolt = "M-BOLT";

    /// <summary>
    /// Grade of a bolt name as written in connection data: "TS M20*60" → S10T, "HTB M20" / "F10T M20" → F10T,
    /// "M16*40" or "SB M16" → M-BOLT. Unknown names default to F10T.
    /// </summary>
    public static string GradeOf(string? name)
    {
        var n = (name ?? "").Trim().ToUpperInvariant();
        if (n.StartsWith("TS", StringComparison.Ordinal) || n.Contains("S10T", StringComparison.Ordinal))
        {
            return S10T;
        }

        if (n.StartsWith("HTB", StringComparison.Ordinal) || n.Contains("F10T", StringComparison.Ordinal) || n.Contains("F8T", StringComparison.Ordinal))
        {
            return n.Contains("F8T", StringComparison.Ordinal) ? "F8T" : F10T;
        }

        if (n.StartsWith('M') || n.StartsWith("SB", StringComparison.Ordinal) || n.Contains("4.6", StringComparison.Ordinal))
        {
            return MBolt;
        }

        return F10T;
    }

    /// <summary>Standard hole: bolt + 2 up to M22, bolt + 3 from M24.</summary>
    public static double HoleFor(double boltDia) => boltDia + (boltDia >= 24 ? 3 : 2);

    /// <summary>Bolt name used in bills of material, e.g. "F10T M20".</summary>
    public static string Name(string grade, double boltDia) => $"{grade} M{boltDia:0}";
}

/// <summary>Where bolt lines go across a section, as used by the HS-STEEL / AIJ gauge tables.</summary>
/// <param name="Face">"web" (front view: across = depth) or "flange" (top view: across = width).</param>
/// <param name="Lines">Positions across the face, from the face edge (web: from the bottom; angle: from the heel).</param>
/// <param name="MaxBolt">Largest bolt the gauge allows.</param>
public sealed record GaugeLines(string Face, IReadOnlyList<double> Lines, double MaxBolt);

/// <summary>Family-appropriate bolt gauges.</summary>
public static class BoltGauges
{
    /// <summary>Angle leg → (g1, g2, max bolt). g2 is the second line further out (0 = single line). AIJ / KS standard gauges.</summary>
    public static IReadOnlyList<(double Leg, double G1, double G2, double MaxBolt)> Angle { get; } =
    [
        (40, 22, 0, 12), (45, 25, 0, 16), (50, 30, 0, 16), (60, 35, 0, 16), (65, 35, 0, 20), (70, 40, 0, 20),
        (75, 40, 0, 22), (80, 45, 0, 22), (90, 50, 0, 24), (100, 55, 0, 24), (120, 50, 35, 24), (125, 50, 35, 24),
        (130, 50, 40, 24), (150, 55, 55, 24), (175, 60, 70, 24), (200, 60, 90, 24),
    ];

    /// <summary>H / T flange width → gauge between the two flange lines (AIJ g1).</summary>
    public static IReadOnlyList<(double Width, double Gauge)> Flange { get; } =
    [
        (100, 60), (125, 75), (150, 90), (175, 105), (200, 120), (250, 150), (300, 150), (350, 140), (400, 140),
    ];

    /// <summary>Gauge entry for an angle leg (the largest tabulated leg not longer than <paramref name="leg"/>).</summary>
    public static (double G1, double G2, double MaxBolt) AngleGauge(double leg)
    {
        var row = Angle.LastOrDefault(r => r.Leg <= leg + 1e-6);
        return row.Leg > 0 ? (row.G1, row.G2, row.MaxBolt) : (Math.Ceiling(leg * 0.55), 0, 10);
    }

    /// <summary>Gauge between the flange bolt lines of an H/T flange of width <paramref name="b"/>.</summary>
    public static double FlangeGauge(double b)
    {
        var row = Flange.LastOrDefault(r => r.Width <= b + 1e-6);
        return row.Width > 0 ? row.Gauge : Math.Round(b * 0.6 / 5) * 5;
    }

    /// <summary>Default bolt size for a member end connection of this profile, limited by its gauge.</summary>
    public static double DefaultBolt(Profile p) => p.Kind switch
    {
        ShapeKind.L => Math.Min(AngleGauge(p.Depth).MaxBolt, p.Depth >= 75 ? 20 : 16),
        ShapeKind.LipChannel or ShapeKind.Z => p.Tw <= 2.3 ? 12 : 16,
        ShapeKind.Flat or ShapeKind.Plate => p.Depth <= 40 ? 12 : p.Depth <= 60 ? 16 : 20,
        ShapeKind.Box or ShapeKind.Pipe => Math.Max(p.Depth, p.Width) <= 75 ? 16 : 20,
        ShapeKind.Channel => p.Depth < 150 ? 16 : p.Depth < 300 ? 20 : 22,
        _ => p.Depth < 200 ? 16 : p.Depth < 400 ? 20 : 22,
    };

    /// <summary>
    /// Bolt lines across the section for an end connection with bolts of <paramref name="bolt"/> mm
    /// (null for families that are connected through an end plate or not bolted at all).
    /// </summary>
    public static GaugeLines? For(Profile p, double bolt)
    {
        var e = Math.Max(1.5 * bolt, 20); // minimum edge distance (sheared edge, rounded practice)
        switch (p.Kind)
        {
            case ShapeKind.L:
                {
                    var (g1, g2, max) = AngleGauge(p.Depth);
                    return new GaugeLines("web", g2 > 0 && p.Depth - (g1 + g2) >= e ? [g1, g1 + g2] : [g1], max);
                }

            case ShapeKind.T:
                {
                    var g = Math.Min(FlangeGauge(p.Width), p.Width - (2 * e));
                    return new GaugeLines("flange", [(p.Width - g) / 2, (p.Width + g) / 2], 24);
                }

            case ShapeKind.I:
            case ShapeKind.Channel:
                {
                    // Web lines inside the clear web (flanges + fillets + edge), pitch 3d rounded to 10.
                    var clear0 = p.Tf + Math.Max(p.RootRadius, 0) + e;
                    var usable = p.Depth - (2 * clear0);
                    var pitch = Math.Ceiling(3 * bolt / 10) * 10;
                    var n = Math.Max(1, (int)Math.Floor(usable / pitch) + 1);
                    n = Math.Min(n, 6);
                    var span = (n - 1) * pitch;
                    var y0 = (p.Depth - span) / 2;
                    return new GaugeLines("web", [.. Enumerable.Range(0, n).Select(i => y0 + (i * pitch))], 24);
                }

            case ShapeKind.LipChannel:
            case ShapeKind.Z:
                {
                    // Purlin / girt cleat holes: a pair centred on the web.
                    double s = p.Depth <= 125 ? 40 : p.Depth <= 200 ? 60 : 100;
                    s = Math.Min(s, p.Depth - (2 * (p.Tw + p.CornerRadius + e)));
                    return s > 0 ? new GaugeLines("web", [(p.Depth - s) / 2, (p.Depth + s) / 2], 16) : new GaugeLines("web", [p.Depth / 2], 16);
                }

            case ShapeKind.Flat:
            case ShapeKind.Plate:
                {
                    var w = p.Depth;
                    if (w < (2 * e) + (3 * bolt) + 10)
                    {
                        return new GaugeLines("web", [w / 2], w);
                    }

                    var g = Math.Floor((w - (2 * e)) / 5) * 5;
                    return new GaugeLines("web", [(w - g) / 2, (w + g) / 2], 24);
                }

            default:
                return null;
        }
    }
}
