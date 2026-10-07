using System.Text.Json.Nodes;

namespace HsSteel.Drafting;

/// <summary>Axis-aligned box (model units) used by the annotation layout engine.</summary>
public readonly record struct Box(double X0, double Y0, double X1, double Y1)
{
    public double W => X1 - X0;

    public double H => Y1 - Y0;

    public bool Overlaps(Box o, double pad = 0) =>
        X0 < o.X1 + pad && o.X0 < X1 + pad && Y0 < o.Y1 + pad && o.Y0 < Y1 + pad;

    public Box Inflate(double v) => new(X0 - v, Y0 - v, X1 + v, Y1 + v);

    public Box Union(Box o) => new(Math.Min(X0, o.X0), Math.Min(Y0, o.Y0), Math.Max(X1, o.X1), Math.Max(Y1, o.Y1));

    public static Box Of(double x0, double y0, double x1, double y1) => new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));

    public override string ToString() => $"[{X0:0.#},{Y0:0.#} - {X1:0.#},{Y1:0.#}]";
}

/// <summary>
/// Bounding boxes of everything a reader has to read on a plan: texts, dimension texts (placed the way the
/// backends place them: <see cref="DimTextPaper"/> mm × scale, 0.4 h off the dimension line, above / left of it) and mark balloons.
/// </summary>
public static class AnnotationBoxes
{
    /// <summary>Dimension text height on paper (mm): the template DIM-100 / Standard value 3.4 (DFT-001).</summary>
    public const double DimTextPaper = HsSteel.Domain.DetailRules.TemplateDimText;

    public static double DimTextHeight(double scale) => DimTextPaper * scale;

    /// <summary>Box of a dimension's text for a plan drawn at <paramref name="scale"/>.</summary>
    public static Box DimText(JsonObject s, double scale)
    {
        static (double X, double Y) P(JsonNode? n) => (n![0]!.GetValue<double>(), n[1]!.GetValue<double>());
        var p1 = P(s["p1"]);
        var p2 = P(s["p2"]);
        var lp = P(s["line_point"]);
        var vertical = Math.Abs((s["rotation"]?.GetValue<double>() ?? 0) - 90) < 1e-6;
        var span = vertical ? Math.Abs(p2.Y - p1.Y) : Math.Abs(p2.X - p1.X);
        var label = s["text"]?.GetValue<string>() ?? DrawPlan.N(span);
        return DimText(vertical, vertical ? lp.X : lp.Y, vertical ? (p1.Y + p2.Y) / 2 : (p1.X + p2.X) / 2, label, scale);
    }

    /// <summary>Text box of a dimension whose line is at <paramref name="line"/> with its middle at <paramref name="mid"/>.</summary>
    public static Box DimText(bool vertical, double line, double mid, string label, double scale)
    {
        var h = DimTextHeight(scale);
        var w = DrawPlan.TextWidth(label, h);
        var off = h * 0.4;
        return vertical
            ? new Box(line - off - h, mid - (w / 2), line - off, mid + (w / 2))
            : new Box(mid - (w / 2), line + off, mid + (w / 2), line + off + h);
    }

    /// <summary>All annotation boxes of a plan (frame title-block texts excluded).</summary>
    public static List<(Box Box, int Index, string What)> Of(DrawPlan d)
    {
        var list = new List<(Box, int, string)>();
        var balloons = new List<(double X, double Y, double R)>();
        for (var i = 0; i < d.Entities.Count; i++)
        {
            var e = d.Entities[i];
            if (e.Type == "circle" && e.Layer == Layers.Mark)
            {
                var c = e.Spec["center"]!.AsArray();
                var r = e.Spec["radius"]!.GetValue<double>();
                double cx = c[0]!.GetValue<double>(), cy = c[1]!.GetValue<double>();
                balloons.Add((cx, cy, r));
                list.Add((new Box(cx - r, cy - r, cx + r, cy + r), i, "balloon"));
            }
        }

        for (var i = 0; i < d.Entities.Count; i++)
        {
            var e = d.Entities[i];
            if (e.Layer == Layers.Frame)
            {
                continue;
            }

            if (e.Type == "text")
            {
                var p = e.Spec["position"]!.AsArray();
                double x = p[0]!.GetValue<double>(), y = p[1]!.GetValue<double>();
                if (e.Layer == Layers.Mark && balloons.Any(b => Math.Abs(b.X - x) < 1e-3 && Math.Abs(b.Y - y) < 1e-3))
                {
                    continue; // the balloon's own label
                }

                var (tx, ty, tw, th) = DrawPlan.TextBox(e.Spec);
                list.Add((new Box(tx, ty, tx + tw, ty + th), i, "text:" + e.Spec["text"]!.GetValue<string>()));
            }
            else if (e.Type == "dimension")
            {
                list.Add((DimText(e.Spec, d.Scale), i, "dim:" + (e.Spec["text"]?.GetValue<string>() ?? "")));
            }
        }

        return list;
    }

    /// <summary>Pairs of overlapping annotation boxes (empty when the plan is clean).</summary>
    public static List<string> Collisions(DrawPlan d, double pad = 0)
    {
        var boxes = Of(d);
        var hits = new List<string>();
        var order = boxes.OrderBy(b => b.Box.X0).ToList();
        for (var i = 0; i < order.Count; i++)
        {
            for (var j = i + 1; j < order.Count && order[j].Box.X0 < order[i].Box.X1 + pad; j++)
            {
                if (order[i].Box.Overlaps(order[j].Box, pad))
                {
                    hits.Add($"{order[i].What} {order[i].Box} x {order[j].What} {order[j].Box}");
                }
            }
        }

        return hits;
    }
}

/// <summary>
/// Deterministic free-space finder: candidates are tried in the given order and the first one that does not
/// touch an existing annotation of the plan or a registered obstacle (view geometry, symbols) wins.
/// </summary>
public sealed class Placer(DrawPlan d, double pad)
{
    private readonly List<Box> _obstacles = [];

    public IReadOnlyList<Box> Obstacles => _obstacles;

    public void Block(Box b) => _obstacles.Add(b);

    public bool Free(Box b) =>
        !_obstacles.Any(o => o.Overlaps(b, pad)) && !AnnotationBoxes.Of(d).Any(o => o.Box.Overlaps(b, pad));

    /// <summary>First free candidate; when none is free the first candidate is returned (and reported by tests).</summary>
    public Box First(IEnumerable<Box> candidates)
    {
        Box? first = null;
        var annotations = AnnotationBoxes.Of(d).Select(a => a.Box).ToList();
        foreach (var c in candidates)
        {
            first ??= c;
            if (!_obstacles.Any(o => o.Overlaps(c, pad)) && !annotations.Any(o => o.Overlaps(c, pad)))
            {
                return c;
            }
        }

        return first ?? default;
    }

    /// <summary>Candidates of size w×h in the horizontal band [y0, y1] near x: closest x offsets first, then other rows.</summary>
    public static IEnumerable<Box> Band(double x, double w, double h, double y0, double y1, double step, double xMin, double xMax)
    {
        var rows = new List<double>();
        for (var y = y1 - h; y >= y0 - 1e-6; y -= step / 2)
        {
            rows.Add(y);
        }

        for (var k = 0; k <= 60; k++)
        {
            foreach (var dx in k == 0 ? new[] { 0.0 } : [k * step, -k * step])
            {
                var x0 = Math.Clamp(x - (w / 2) + dx, xMin, Math.Max(xMin, xMax - w));
                foreach (var y in rows)
                {
                    yield return new Box(x0, y, x0 + w, y + h);
                }
            }
        }
    }
}
