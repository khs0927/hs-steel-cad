using HsSteel.Domain;
using HsSteel.Modeling;

namespace HsSteel.Drafting;

/// <summary>
/// Maps true positions along a member to drawn positions. Regions around features (holes, plates, ends) are kept
/// at true scale; the stretches between them are replaced by fixed-width breaks when the member does not fit.
/// Dimensions always show true values.
/// </summary>
public sealed class LengthMap
{
    private readonly List<(double A, double B)> _keep;

    private LengthMap(double length, List<(double A, double B)> keep, double gapDrawn)
    {
        Length = length;
        GapDrawn = gapDrawn;
        _keep = keep;
    }

    /// <summary>Unbroken map.</summary>
    public LengthMap(double length)
        : this(length, [(0, length)], 0)
    {
    }

    public double Length { get; }

    public double GapDrawn { get; }

    public bool Broken => _keep.Count > 1;

    /// <summary>True-length intervals drawn at true scale.</summary>
    public IEnumerable<(double A, double B)> Segments => _keep;

    public double DrawnLength => _keep.Sum(k => k.B - k.A) + ((_keep.Count - 1) * GapDrawn);

    /// <summary>Drawn x of every break centre.</summary>
    public IEnumerable<double> Breaks
    {
        get
        {
            double x = 0;
            for (var i = 0; i + 1 < _keep.Count; i++)
            {
                x += _keep[i].B - _keep[i].A;
                yield return x + (GapDrawn / 2);
                x += GapDrawn;
            }
        }
    }

    public double Map(double t)
    {
        double x = 0;
        for (var i = 0; i < _keep.Count; i++)
        {
            var (a, b) = _keep[i];
            if (t <= b || i == _keep.Count - 1)
            {
                return t < a ? x - (GapDrawn / 2) : x + (t - a);
            }

            x += b - a;
            if (i + 1 < _keep.Count && t < _keep[i + 1].A)
            {
                return x + (GapDrawn / 2);
            }

            x += GapDrawn;
        }

        return t;
    }

    /// <summary>
    /// Builds a map no longer than <paramref name="budget"/> when possible: keeps [f - margin, f + margin] around every
    /// feature and both ends, merges overlapping zones, breaks the rest, then gives spare budget back to the ends.
    /// </summary>
    public static LengthMap For(double length, double budget, IEnumerable<double> features, double margin, double gapDrawn)
    {
        if (length <= budget)
        {
            return new LengthMap(length);
        }

        var zones = features.Where(f => f >= 0 && f <= length)
            .Select(f => (A: Math.Max(0, f - margin), B: Math.Min(length, f + margin)))
            .Append((A: 0.0, B: Math.Min(length, margin)))
            .Append((A: Math.Max(0, length - margin), B: length))
            .OrderBy(z => z.A)
            .ToList();
        var keep = new List<(double A, double B)>();
        foreach (var z in zones)
        {
            // Merge zones whose gap is not worth a break (shorter than two break widths).
            if (keep.Count > 0 && z.A - keep[^1].B < 2 * gapDrawn)
            {
                keep[^1] = (keep[^1].A, Math.Max(keep[^1].B, z.B));
            }
            else
            {
                keep.Add(z);
            }
        }

        if (keep.Count == 1)
        {
            return new LengthMap(length);
        }

        // Give spare budget back, growing every kept zone evenly into its neighbouring gaps.
        var drawn = keep.Sum(k => k.B - k.A) + ((keep.Count - 1) * gapDrawn);
        var spare = budget - drawn;
        if (spare > 0)
        {
            var per = spare / (2 * (keep.Count - 1));
            for (var i = 0; i + 1 < keep.Count; i++)
            {
                var gap = keep[i + 1].A - keep[i].B;
                var grow = Math.Min(per, gap / 2);
                keep[i] = (keep[i].A, keep[i].B + grow);
                keep[i + 1] = (keep[i + 1].A - grow, keep[i + 1].B);
            }

            var merged = new List<(double A, double B)>();
            foreach (var k in keep)
            {
                if (merged.Count > 0 && k.A - merged[^1].B < 2 * gapDrawn)
                {
                    merged[^1] = (merged[^1].A, k.B);
                }
                else
                {
                    merged.Add(k);
                }
            }

            keep = merged;
        }

        return keep.Count == 1 ? new LengthMap(length) : new LengthMap(length, keep, gapDrawn);
    }
}

/// <summary>Draws the standard views of a straight member: front (depth visible), top (width visible), section.</summary>
public static class MemberViews
{
    public static void Front(DrawPlan d, Profile p, LengthMap map, double x, double y, IEnumerable<Hole> holes, string layer = Layers.Outline)
    {
        var (vis, hid) = p.FrontLines();
        Body(d, map, x, y, p.Depth, vis, hid, layer);
        if (p.FrontCenterLine)
        {
            foreach (var (a, b) in map.Segments)
            {
                d.Line(Layers.Center, x + map.Map(a) - (p.Depth * 0.05), y + (p.Depth / 2), x + map.Map(b) + (p.Depth * 0.05), y + (p.Depth / 2));
            }
        }

        foreach (var h in holes.Where(h => h.Face == HoleFace.Web))
        {
            d.Circle(Layers.Hole, x + map.Map(h.X), y + h.Across, h.Dia / 2);
        }
    }

    public static void Top(DrawPlan d, Profile p, LengthMap map, double x, double y, IEnumerable<Hole> holes, string layer = Layers.Outline)
    {
        var (vis, hid) = p.TopLines();
        Body(d, map, x, y, p.Width, vis, hid, layer);
        foreach (var h in holes.Where(h => h.Face is HoleFace.TopFlange or HoleFace.BottomFlange).DistinctBy(h => (Math.Round(h.X, 1), Math.Round(h.Across, 1))))
        {
            d.Circle(Layers.Hole, x + map.Map(h.X), y + h.Across, h.Dia / 2);
        }
    }

    /// <summary>Cross-section with its outline loops, centred at (cx, cy).</summary>
    public static void Section(DrawPlan d, Profile p, double cx, double cy, double scaleUp = 1)
    {
        double ox = cx - (p.Width * scaleUp / 2), oy = cy - (p.Depth * scaleUp / 2);
        foreach (var loop in p.Section())
        {
            if (loop.Radius > 0)
            {
                d.Circle(Layers.Outline, ox + (loop.Center.X * scaleUp), oy + (loop.Center.Y * scaleUp), loop.Radius * scaleUp);
            }
            else
            {
                d.Polyline(Layers.Outline, true, [.. loop.Points.Select(pt => (ox + (pt.X * scaleUp), oy + (pt.Y * scaleUp)))]);
            }
        }
    }

    private static void Body(DrawPlan d, LengthMap map, double x, double y, double h, IReadOnlyList<double> vis, IReadOnlyList<double> hid, string layer)
    {
        foreach (var (a, b) in map.Segments)
        {
            double xa = x + map.Map(a), xb = x + map.Map(b);
            foreach (var o in vis)
            {
                d.Line(layer, xa, y + o, xb, y + o);
            }

            foreach (var o in hid)
            {
                d.Line(Layers.Hidden, xa, y + o, xb, y + o);
            }
        }

        d.Line(layer, x, y, x, y + h);
        d.Line(layer, x + map.DrawnLength, y, x + map.DrawnLength, y + h);
        foreach (var b in map.Breaks)
        {
            Break(d, x + b, y - (h * 0.12), y + (h * 1.12), map.GapDrawn);
        }
    }

    private static void Break(DrawPlan d, double x, double y0, double y1, double gap)
    {
        var h = y1 - y0;
        var z = gap * 0.3;
        foreach (var bx in new[] { x - (gap / 2), x + (gap / 2) })
        {
            d.Polyline(Layers.Break, false, (bx, y0), (bx, y0 + (h * 0.42)), (bx + z, y0 + (h * 0.48)), (bx - z, y0 + (h * 0.54)), (bx, y0 + (h * 0.6)), (bx, y1));
        }
    }
}
