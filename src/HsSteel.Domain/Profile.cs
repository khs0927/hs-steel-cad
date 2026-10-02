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

    public double Depth => Kind switch
    {
        ShapeKind.Pipe or ShapeKind.Round => D[0],
        ShapeKind.Flat => D[0],
        ShapeKind.Plate => D[1],
        _ => D[0],
    };

    public double Width => Kind switch
    {
        ShapeKind.Pipe or ShapeKind.Round => D[0],
        ShapeKind.Flat => D[1],
        ShapeKind.Plate => D[0],
        ShapeKind.L or ShapeKind.Channel or ShapeKind.LipChannel or ShapeKind.Z or ShapeKind.Box or ShapeKind.Block => D[1],
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

    public double Radius => Kind is ShapeKind.I or ShapeKind.T && D.Count > 4 ? D[4] : 0;

    public double WeightFor(double lengthMm) => Math.Round(UnitWeight * lengthMm / 1000.0, 2);

    public double PaintFor(double lengthMm) => Math.Round(PaintArea * lengthMm / 1000.0, 3);

    /// <summary>Cross-section loops (outer first).</summary>
    public IReadOnlyList<Loop> Section()
    {
        double h = Depth, b = Width, tw = Tw, tf = Tf;
        V2 P(double x, double y) => new(x, y);
        switch (Kind)
        {
            case ShapeKind.I:
                {
                    double c = b / 2, w = tw / 2;
                    return [new([P(0, 0), P(b, 0), P(b, tf), P(c + w, tf), P(c + w, h - tf), P(b, h - tf), P(b, h), P(0, h), P(0, h - tf), P(c - w, h - tf), P(c - w, tf), P(0, tf)])];
                }

            case ShapeKind.T:
                {
                    double c = b / 2, w = tw / 2;
                    return [new([P(c - w, 0), P(c + w, 0), P(c + w, h - tf), P(b, h - tf), P(b, h), P(0, h), P(0, h - tf), P(c - w, h - tf)])];
                }

            case ShapeKind.L:
                return [new([P(0, 0), P(b, 0), P(b, tw), P(tw, tw), P(tw, h), P(0, h)])];
            case ShapeKind.Channel:
                return [new([P(0, 0), P(b, 0), P(b, tf), P(tw, tf), P(tw, h - tf), P(b, h - tf), P(b, h), P(0, h)])];
            case ShapeKind.LipChannel:
                {
                    var c = D[2];
                    return [new([P(0, 0), P(b, 0), P(b, c), P(b - tw, c), P(b - tw, tw), P(tw, tw), P(tw, h - tw), P(b - tw, h - tw), P(b - tw, h - c), P(b, h - c), P(b, h), P(0, h)])];
                }

            case ShapeKind.Z:
                {
                    var c = D[2];
                    double hb = b / 2;
                    return [new([P(hb, 0), P(b, 0), P(b, c), P(b - tw, c), P(b - tw, tw), P(hb + tw, tw), P(hb + tw, h), P(0, h), P(0, h - c), P(tw, h - c), P(tw, h - tw), P(hb, h - tw)])];
                }

            case ShapeKind.Box:
                return [new([P(0, 0), P(b, 0), P(b, h), P(0, h)]), new([P(tw, tw), P(b - tw, tw), P(b - tw, h - tw), P(tw, h - tw)])];
            case ShapeKind.Pipe:
                return [Loop.Circle(P(h / 2, h / 2), h / 2), Loop.Circle(P(h / 2, h / 2), (h / 2) - tw)];
            case ShapeKind.Round:
                return [Loop.Circle(P(h / 2, h / 2), h / 2)];
            default:
                return [new([P(0, 0), P(b, 0), P(b, h), P(0, h)])];
        }
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
        ShapeKind.LipChannel or ShapeKind.Z => ([0, Width], [Tw]),
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

        return new Profile(r.Spec, r.Family, kind, d, r.UnitWeight, r.PaintArea);
    }

    /// <summary>Parses a spec when no table row exists. Weight from geometry x 7.85e-6 kg/mm³; paint from perimeter.</summary>
    public static Profile Parse(string spec)
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
            (kind, family, d) = (ShapeKind.I, "H-BEAM", [n[0], n[1], n[2], n[3], 0]);
        }
        else if (s.StartsWith('L') && n.Length >= 3)
        {
            (kind, family, d) = (ShapeKind.L, "ANGLE", [n[0], n[1], n[2]]);
        }
        else if (s.StartsWith('[') && n.Length >= 4)
        {
            (kind, family, d) = (ShapeKind.Channel, "CHANNEL", [n[0], n[1], n[2], n[3]]);
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
        else if (s.StartsWith('F') && n.Length >= 2)
        {
            (kind, family, d) = (ShapeKind.Flat, "FLAT-BAR", [n[0], n[1]]);
        }
        else
        {
            throw new FormatException($"Unknown section spec '{spec}'.");
        }

        var p = new Profile(spec, family, kind, d, 0, 0);
        var area = Area(p);
        return p with { UnitWeight = Math.Round(area * 7.85e-3, 2), PaintArea = Math.Round(Perimeter(p) / 1000.0, 3) };
    }

    /// <summary>Net cross-section area in mm² from the outline loops (fillets ignored).</summary>
    public static double Area(Profile p)
    {
        var loops = p.Section();
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
            "[" => ShapeKind.Channel,
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
