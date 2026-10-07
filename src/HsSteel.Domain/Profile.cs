using System.Globalization;
using System.Text.RegularExpressions;
using HsSteel.Assets;

namespace HsSteel.Domain;

/// <summary>Cross-section kinds covering every HS-STEEL section table.</summary>
public enum ShapeKind
{
    /// <summary>H, BH, LH, PH (PEB), I. Dims: h, b, tw, tf, r.</summary>
    I,

    /// <summary>T-bar (stem down). Dims: h, b, tw, tf, r.</summary>
    T,

    /// <summary>Angle. Dims: a (vertical leg), b (horizontal leg), t.</summary>
    L,

    /// <summary>Hot-rolled channel [. Dims: h, b, tw, tf.</summary>
    Channel,

    /// <summary>Light lipped channel C. Dims: h, b, c (lip), t.</summary>
    LipChannel,

    /// <summary>Z section. Dims: h, b, c (lip), t.</summary>
    Z,

    /// <summary>Square/rectangular pipe ㅁ. Dims: h, b, t.</summary>
    Box,

    /// <summary>Round steel pipe. Dims: d, t.</summary>
    Pipe,

    /// <summary>Flat bar. Dims: b (width), t.</summary>
    Flat,

    /// <summary>Round bar. Dims: d.</summary>
    Round,

    /// <summary>Plate / expanded metal. Dims: t, w.</summary>
    Plate,

    /// <summary>Grating, deck plate, concrete, user shapes: drawn as a bounding rectangle. Dims: h, b.</summary>
    Block,
}

/// <summary>A closed loop of a cross-section: a polygon, or a circle when <see cref="Radius"/> &gt; 0.</summary>
public sealed record Loop(IReadOnlyList<V2> Points, V2 Center = default, double Radius = 0)
{
    public static Loop Circle(V2 c, double r) => new([], c, r);
}

/// <summary>
/// A steel profile with the geometry needed for drawing. Coordinates of the cross-section: X across the
/// width (0..Width), Y up the depth (0..Depth). The front view looks at the side that shows the depth,
/// the top view at the side that shows the width.
/// </summary>
public sealed record Profile(string Spec, string Family, ShapeKind Kind, IReadOnlyList<double> D, double UnitWeight, double PaintArea)
{
    private static readonly Regex Numbers = new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);

    /// <summary>Overall depth. Tapered PEB members (PH a-b): the larger end depth.</summary>
    public double Depth => Kind switch
    {
        ShapeKind.Pipe or ShapeKind.Round => D[0],
        ShapeKind.Flat => D[0],
        ShapeKind.Plate => D[1],
        ShapeKind.I when IsTapered => Math.Max(D[0], D[5]),
        _ => D[0],
    };

    /// <summary>Overall width. Z: the flanges stick out on opposite sides of the web (2b - t).</summary>
    public double Width => Kind switch
    {
        ShapeKind.Pipe or ShapeKind.Round => D[0],
        ShapeKind.Flat => D[1],
        ShapeKind.Plate => D[0],
        ShapeKind.Z => (2 * D[1]) - D[3],
        _ => D[1],
    };

    /// <summary>Web thickness for I/T/Channel, wall thickness otherwise.</summary>
    public double Tw => Kind switch
    {
        ShapeKind.I or ShapeKind.T or ShapeKind.Channel => D[2],
        ShapeKind.L or ShapeKind.Box => D[2],
        ShapeKind.LipChannel or ShapeKind.Z => D[3],
        ShapeKind.Pipe => D[1],
        ShapeKind.Flat => D[1],
        ShapeKind.Plate => D[0],
        _ => 0,
    };

    public double Tf => Kind switch
    {
        ShapeKind.I or ShapeKind.T or ShapeKind.Channel => D[3],
        _ => Tw,
    };

    /// <summary>Table radius column (M6) of I/T sections. For built-up BH/LH/PH this column is the weld size, not a fillet.</summary>
    public double Radius => Kind is ShapeKind.I or ShapeKind.T && D.Count > 4 ? D[4] : 0;

    /// <summary>True for hot-rolled sections (H, I, T, L, [): their outlines carry the table's root/toe radii.</summary>
    public bool IsRolled => Kind switch
    {
        ShapeKind.I => Spec.StartsWith('H') || Spec.StartsWith('I'),
        ShapeKind.T or ShapeKind.L or ShapeKind.Channel => true,
        _ => false,
    };

    /// <summary>Root fillet radius r1 (web-to-flange / leg-to-leg) of rolled sections; 0 for welded or cold-formed.</summary>
    public double RootRadius => IsRolled && D.Count > 4 ? D[4] : 0;

    /// <summary>Flange-toe radius r2 of rolled angles, channels and I-beams (table column M7).</summary>
    public double ToeRadius => IsRolled && Kind is ShapeKind.L or ShapeKind.Channel or ShapeKind.I && D.Count > 5 ? D[5] : 0;

    /// <summary>Outer bend radius of cold-formed sections (C, Z, square/rectangular tube): table column M7.</summary>
    public double CornerRadius => Kind is ShapeKind.LipChannel or ShapeKind.Z or ShapeKind.Box && D.Count > 5 ? Math.Min(D[5], Math.Min(Depth, Width) / 2) : 0;

    /// <summary>Lip length of C / Z sections (0 when unlipped).</summary>
    public double Lip => Kind is ShapeKind.LipChannel or ShapeKind.Z ? D[2] : 0;

    /// <summary>Tapered PEB member: start depth D[0], end depth D[5].</summary>
    public bool IsTapered => Kind == ShapeKind.I && Spec.StartsWith("PH", StringComparison.OrdinalIgnoreCase) && D.Count > 5 && D[5] > 0 && Math.Abs(D[5] - D[0]) > 1e-6;

    /// <summary>Depth at the start / end of the member (both = Depth unless tapered).</summary>
    public (double Start, double End) DepthEnds => IsTapered ? (D[0], D[5]) : (Depth, Depth);

    public double WeightFor(double lengthMm) => Math.Round(UnitWeight * lengthMm / 1000.0, 2);

    public double PaintFor(double lengthMm) => Math.Round(PaintArea * lengthMm / 1000.0, 3);

    /// <summary>
    /// Cross-section loops (outer first), with the table's root/toe fillets and cold-formed bend radii
    /// tessellated into the polygon (8 segments per quarter circle). Tapered members: the section at the deep end.
    /// </summary>
    public IReadOnlyList<Loop> Section() => SectionAt(Depth);

    /// <summary>Cross-section loops for a given overall depth (used for the mean section of tapered members).</summary>
    public IReadOnlyList<Loop> SectionAt(double depth)
    {
        double h = depth, b = Width, tw = Tw, tf = Tf, r1 = RootRadius, r2 = ToeRadius;
        V2 P(double x, double y) => new(x, y);
        static (V2, double) C(double x, double y, double r = 0) => (new V2(x, y), r);
        switch (Kind)
        {
            case ShapeKind.I:
                {
                    double c = b / 2, w = tw / 2;
                    return [Round([C(0, 0), C(b, 0), C(b, tf, r2), C(c + w, tf, r1), C(c + w, h - tf, r1), C(b, h - tf, r2), C(b, h), C(0, h), C(0, h - tf, r2), C(c - w, h - tf, r1), C(c - w, tf, r1), C(0, tf, r2)])];
                }

            case ShapeKind.T:
                {
                    double c = b / 2, w = tw / 2;
                    return [Round([C(c - w, 0), C(c + w, 0), C(c + w, h - tf, r1), C(b, h - tf), C(b, h), C(0, h), C(0, h - tf), C(c - w, h - tf, r1)])];
                }

            case ShapeKind.L:
                return [Round([C(0, 0), C(b, 0), C(b, tw, r2), C(tw, tw, r1), C(tw, h, r2), C(0, h)])];
            case ShapeKind.Channel:
                return [Round([C(0, 0), C(b, 0), C(b, tf, r2), C(tw, tf, r1), C(tw, h - tf, r1), C(b, h - tf, r2), C(b, h), C(0, h)])];
            case ShapeKind.LipChannel:
                {
                    double c = Lip, ro = CornerRadius, ri = Math.Max(0, ro - tw);
                    if (c <= tw + 1e-6)
                    {
                        return [Round([C(0, 0, ro), C(b, 0), C(b, tw), C(tw, tw, ri), C(tw, h - tw, ri), C(b, h - tw), C(b, h), C(0, h, ro)])];
                    }

                    return [Round([C(0, 0, ro), C(b, 0, ro), C(b, c), C(b - tw, c), C(b - tw, tw, ri), C(tw, tw, ri), C(tw, h - tw, ri), C(b - tw, h - tw, ri), C(b - tw, h - c), C(b, h - c), C(b, h, ro), C(0, h, ro)])];
                }

            case ShapeKind.Z:
                {
                    // Web at x = bf - t .. bf; the bottom flange runs right to 2bf - t, the top flange left to 0.
                    double c = Lip, bf = D[1], ro = CornerRadius, ri = Math.Max(0, ro - tw);
                    if (c <= tw + 1e-6)
                    {
                        return [Round([C(bf - tw, 0, ro), C(b, 0), C(b, tw), C(bf, tw, ri), C(bf, h, ro), C(0, h), C(0, h - tw), C(bf - tw, h - tw, ri)])];
                    }

                    return [Round([C(bf - tw, 0, ro), C(b, 0, ro), C(b, c), C(b - tw, c), C(b - tw, tw, ri), C(bf, tw, ri), C(bf, h, ro), C(0, h, ro), C(0, h - c), C(tw, h - c), C(tw, h - tw, ri), C(bf - tw, h - tw, ri)])];
                }

            case ShapeKind.Box:
                {
                    double ro = CornerRadius, ri = Math.Max(0, ro - tw);
                    return [Round([C(0, 0, ro), C(b, 0, ro), C(b, h, ro), C(0, h, ro)]), Round([C(tw, tw, ri), C(b - tw, tw, ri), C(b - tw, h - tw, ri), C(tw, h - tw, ri)])];
                }

            case ShapeKind.Pipe:
                return [Loop.Circle(P(h / 2, h / 2), h / 2), Loop.Circle(P(h / 2, h / 2), (h / 2) - tw)];
            case ShapeKind.Round:
                return [Loop.Circle(P(h / 2, h / 2), h / 2)];
            default:
                return [new([P(0, 0), P(b, 0), P(b, h), P(0, h)])];
        }
    }

    /// <summary>Replaces every corner with radius r &gt; 0 by a tessellated tangent arc (convex and concave corners alike).</summary>
    private static Loop Round(IReadOnlyList<(V2 P, double R)> corners)
    {
        const int perQuarter = 8;
        var pts = new List<V2>();
        var n = corners.Count;
        for (var i = 0; i < n; i++)
        {
            var (p, r) = corners[i];
            var u1 = corners[(i + n - 1) % n].P - p;
            var u2 = corners[(i + 1) % n].P - p;
            double l1 = u1.Length, l2 = u2.Length;
            if (r <= 1e-9 || l1 < 1e-9 || l2 < 1e-9)
            {
                pts.Add(p);
                continue;
            }

            u1 *= 1 / l1;
            u2 *= 1 / l2;
            var half = Math.Acos(Math.Clamp((u1.X * u2.X) + (u1.Y * u2.Y), -1, 1)) / 2;
            if (half < 1e-6 || half > (Math.PI / 2) - 1e-6)
            {
                pts.Add(p);
                continue;
            }

            // Tangent length limited to half of each adjacent edge so neighbouring arcs never cross.
            var t = Math.Min(r / Math.Tan(half), Math.Min(l1, l2) / 2);
            var rr = t * Math.Tan(half);
            var bis = u1 + u2;
            bis *= 1 / bis.Length;
            var c = p + (bis * (rr / Math.Sin(half)));
            var t1 = p + (u1 * t);
            var t2 = p + (u2 * t);
            double a0 = Math.Atan2(t1.Y - c.Y, t1.X - c.X), a1 = Math.Atan2(t2.Y - c.Y, t2.X - c.X);
            var sweep = a1 - a0;
            while (sweep > Math.PI)
            {
                sweep -= 2 * Math.PI;
            }

            while (sweep < -Math.PI)
            {
                sweep += 2 * Math.PI;
            }

            var segs = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) * perQuarter));
            for (var k = 0; k <= segs; k++)
            {
                var ang = a0 + (sweep * k / segs);
                pts.Add(new V2(Math.Round(c.X + (rr * Math.Cos(ang)), 4), Math.Round(c.Y + (rr * Math.Sin(ang)), 4)));
            }
        }

        return new Loop(pts);
    }

    /// <summary>Lines along the member in the front view: offsets (0..Depth) that are visible / hidden.</summary>
    public (IReadOnlyList<double> Visible, IReadOnlyList<double> Hidden) FrontLines() => Kind switch
    {
        ShapeKind.I => ([0, Tf, Depth - Tf, Depth], []),
        ShapeKind.T => ([0, Depth - Tf, Depth], []),
        ShapeKind.L => ([0, Tw, Depth], []),
        ShapeKind.Channel => ([0, Depth], [Tf, Depth - Tf]),
        ShapeKind.LipChannel or ShapeKind.Z => ([0, Depth], [Tw, Depth - Tw]),
        ShapeKind.Box or ShapeKind.Pipe => ([0, Depth], [Tw, Depth - Tw]),
        _ => ([0, Depth], []),
    };

    /// <summary>Lines along the member in the top view: offsets (0..Width) that are visible / hidden.</summary>
    public (IReadOnlyList<double> Visible, IReadOnlyList<double> Hidden) TopLines() => Kind switch
    {
        ShapeKind.I => ([0, Width], [(Width - Tw) / 2, (Width + Tw) / 2]),
        ShapeKind.T => ([0, Width], [(Width - Tw) / 2, (Width + Tw) / 2]),
        ShapeKind.L => ([0, Tw, Width], []),
        ShapeKind.Channel => ([0, Width], [Tw]),
        ShapeKind.LipChannel => ([0, Width], [Tw]),
        ShapeKind.Z => ([0, D[1], Width], [D[1] - Tw]),
        ShapeKind.Box or ShapeKind.Pipe => ([0, Width], [Tw, Width - Tw]),
        _ => ([0, Width], []),
    };

    /// <summary>True when the front view shows a centre line (symmetric about mid-depth).</summary>
    public bool FrontCenterLine => Kind is ShapeKind.I or ShapeKind.Box or ShapeKind.Pipe or ShapeKind.Round or ShapeKind.Channel or ShapeKind.LipChannel;

    public static Profile FromRecord(SectionRecord r)
    {
        var kind = KindOf(r.Shape, r.Family);
        if (r.Shape == "%%C")
        {
            // Pipe rows carry a wall thickness (M4); round bars do not. The row's family label is not reliable.
            kind = r.M.Count > 2 && r.M[2] > 0 && r.M[2] < r.M[0] / 2 ? ShapeKind.Pipe : ShapeKind.Round;
        }
        var d = r.M.ToArray();
        if (kind == ShapeKind.Pipe)
        {
            d = [r.M[0], r.M[2]];
        }
        else if (r.Shape == "F-" && r.M.Count > 3)
        {
            d = [r.M[0], r.M[3]]; // grating: bearing bar height x panel width (M2 x M5)
        }

        return new Profile(r.Spec, r.Family, kind, d, r.UnitWeight, r.PaintArea);
    }

    /// <summary>
    /// Parses a spec when no table row exists. Paint from perimeter. Weight (WT-002): H/I/BH/LH/PH use the legacy
    /// 단중.xlsx formula ROUND((2·B·tf + (H−2·tf)·tw + (4−π)·r²)·0.00785, 1) with r = tw for built-up sections and the
    /// rolled root radius otherwise; angles and channels get their root fillet(s) in the outline and weigh
    /// area × 7.85e-3 (2 dp).
    /// </summary>
    /// <param name="spec">Section spec, e.g. "H400x200x8x13".</param>
    /// <param name="rootRadius">
    /// Root fillet radius of a rolled H/I, channel or angle (e.g. from the nearest catalog row). When null:
    /// H/I ≈ 0.059·√(H·B) rounded (fit to H-BEAM.dat), channel = tf, angle = t. Ignored for built-up BH/LH/PH (r = tw).
    /// </param>
    public static Profile Parse(string spec, double? rootRadius = null)
    {
        var s = spec.Trim();
        var n = Numbers.Matches(s).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
        ShapeKind kind;
        string family;
        double[] d;
        if (s.StartsWith("PL", StringComparison.OrdinalIgnoreCase) && n.Length >= 1)
        {
            (kind, family, d) = (ShapeKind.Plate, "PLATE", [n[0], n.Length > 1 ? n[1] : 0]);
        }
        else if (Regex.IsMatch(s, "^(BH|LH|PH|H|I)", RegexOptions.IgnoreCase) && n.Length >= 4)
        {
            var rolled = s.StartsWith('H') || s.StartsWith('I'); // same test as IsRolled
            var r = rolled ? rootRadius ?? Math.Round(0.059 * Math.Sqrt(n[0] * n[1])) : n[2];
            (kind, family, d) = (ShapeKind.I, "H-BEAM", [n[0], n[1], n[2], n[3], r]);
        }
        else if (s.StartsWith('L') && n.Length >= 3)
        {
            (kind, family, d) = (ShapeKind.L, "ANGLE", [n[0], n[1], n[2], n[2], rootRadius ?? n[2], 0]);
        }
        else if ((s.StartsWith('[') || s.StartsWith('ㄷ')) && n.Length >= 4)
        {
            (kind, family, d) = (ShapeKind.Channel, "CHANNEL", [n[0], n[1], n[2], n[3], rootRadius ?? n[3], 0]);
        }
        else if (s.StartsWith('C') && n.Length >= 4)
        {
            (kind, family, d) = (ShapeKind.LipChannel, "C-CHANNEL", [n[0], n[1], n[2], n[3]]);
        }
        else if (s.StartsWith('ㅁ') && n.Length >= 3)
        {
            (kind, family, d) = (ShapeKind.Box, "SQ-PIPE", [n[0], n[1], n[2]]);
        }
        else if ((s.StartsWith('Φ') || s.StartsWith('P')) && n.Length >= 2)
        {
            (kind, family, d) = (ShapeKind.Pipe, "STEEL-PIPE", [n[0], n[1]]);
        }
        else if (s.StartsWith('T') && n.Length >= 4)
        {
            (kind, family, d) = (ShapeKind.T, "T-BAR", [n[0], n[1], n[2], n[3], 0]);
        }
        else if (s.StartsWith('Z') && n.Length >= 4)
        {
            (kind, family, d) = (ShapeKind.Z, "Z-BAR", [n[0], n[1], n[2], n[3]]);
        }
        else if (s.StartsWith("RB", StringComparison.OrdinalIgnoreCase) && n.Length >= 1)
        {
            (kind, family, d) = (ShapeKind.Round, "ROUND-BAR", [n[0]]);
        }
        else if (s.StartsWith('F') && n.Length >= 2)
        {
            (kind, family, d) = (ShapeKind.Flat, "FLAT-BAR", [n[0], n[1]]);
        }
        else
        {
            throw new FormatException($"Unknown section spec '{spec}'.");
        }

        var p = new Profile(spec, family, kind, d, 0, 0);
        var weight = kind == ShapeKind.I ? HUnitWeight(d[0], d[1], d[2], d[3], d[4]) : Math.Round(Area(p) * 7.85e-3, 2);
        return p with { UnitWeight = weight, PaintArea = Math.Round(Perimeter(p) / 1000.0, 3) };
    }

    /// <summary>
    /// WT-002: H/BH unit weight [kg/m] = ROUND((2·B·tf + (H−2·tf)·tw + (4−π)·r²)·0.00785, 1); the (4−π)·r² term is the
    /// four root fillets (BH: r = tw). Matches BH-BEAM.dat 159/159 and H-BEAM.dat 81/81 (RULES_CATALOG).
    /// </summary>
    public static double HUnitWeight(double h, double b, double tw, double tf, double r) =>
        Math.Round(((2 * b * tf) + ((h - (2 * tf)) * tw) + ((4 - Math.PI) * r * r)) * 0.00785, 1, MidpointRounding.AwayFromZero);

    /// <summary>Net cross-section area in mm² from the outline loops (fillets and bends included; tapered: mean depth).</summary>
    public static double Area(Profile p)
    {
        var loops = p.IsTapered ? p.SectionAt((p.D[0] + p.D[5]) / 2) : p.Section();
        double a = 0;
        for (var i = 0; i < loops.Count; i++)
        {
            var la = loops[i].Radius > 0 ? Math.PI * loops[i].Radius * loops[i].Radius : Math.Abs(Shoelace(loops[i].Points));
            a += i == 0 ? la : -la;
        }

        return a;
    }

    private static double Perimeter(Profile p)
    {
        var o = p.Section()[0];
        if (o.Radius > 0)
        {
            return 2 * Math.PI * o.Radius;
        }

        double s = 0;
        for (var i = 0; i < o.Points.Count; i++)
        {
            s += (o.Points[(i + 1) % o.Points.Count] - o.Points[i]).Length;
        }

        return s;
    }

    private static double Shoelace(IReadOnlyList<V2> p)
    {
        double s = 0;
        for (var i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            s += (a.X * b.Y) - (b.X * a.Y);
        }

        return s / 2;
    }

    private static ShapeKind KindOf(string shape, string family)
    {
        var f = family.ToUpperInvariant();
        return shape.ToUpperInvariant() switch
        {
            "H" or "BH" or "LH" or "PH" or "I" => ShapeKind.I,
            "T" => ShapeKind.T,
            "L" or "2L" => ShapeKind.L, // 2L: double angle, table weight is per angle
            "[" or "ㄷ" => ShapeKind.Channel,
            "C" => ShapeKind.LipChannel,
            "Z" => ShapeKind.Z,
            "ㅁ" => ShapeKind.Box,
            "F" => ShapeKind.Flat,
            "PL-" or "EXP-" => ShapeKind.Plate,
            "%%C" when f.Contains("PIPE") => ShapeKind.Pipe,
            "%%C" => ShapeKind.Round,
            _ => ShapeKind.Block,
        };
    }
}
