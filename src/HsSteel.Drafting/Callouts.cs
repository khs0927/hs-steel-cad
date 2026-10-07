using System.Text.Json.Nodes;
using HsSteel.Domain;
using HsSteel.Modeling;

namespace HsSteel.Drafting;

/// <summary>Weld symbol kinds. The block names are placeholders a backend may swap for the legacy HS-STEEL blocks.</summary>
public enum WeldKind
{
    Fillet,
    Groove,
}

/// <summary>Callouts: bolt notes, weld symbols, member marks and the enlarged section view.</summary>
public static class Callouts
{
    public const string FilletBlock = "HS_WELD_FILLET";
    public const string GrooveBlock = "HS_WELD_GROOVE";

    /// <summary>Bolt size for a hole: hole = bolt + 2 up to M22, bolt + 3 from M24 (hole 27) up (KS / HS-STEEL rule).</summary>
    public static double BoltDia(double holeDia) => holeDia - (holeDia >= 27 ? 3 : 2);

    /// <summary>"6-M20 HTB (Ø22)".</summary>
    public static string BoltNote(int count, double holeDia, string type = "HTB") =>
        $"{count}-M{Annotate.F(BoltDia(holeDia))} {type} (Ø{Annotate.F(holeDia)})";

    /// <summary>Groups holes into clusters along the member (gap larger than <paramref name="split"/> starts a new one).</summary>
    public static List<List<Hole>> Clusters(IEnumerable<Hole> holes, double split = 300)
    {
        var res = new List<List<Hole>>();
        foreach (var h in holes.OrderBy(h => h.X).ThenBy(h => h.Across))
        {
            if (res.Count == 0 || h.X - res[^1][^1].X > split)
            {
                res.Add([]);
            }

            res[^1].Add(h);
        }

        return res;
    }

    /// <summary>Fillet leg for a plate of thickness t welded to a member (min 6, ~0.7 t, never above t).</summary>
    public static double FilletSize(double t) => Math.Min(t, Math.Max(6, Math.Ceiling(t * 0.7)));

    /// <summary>A text note with a leader from <paramref name="anchor"/>, placed by <paramref name="placer"/> among the candidates.</summary>
    public static Box Note(DrawPlan d, Placer placer, string text, double h, (double X, double Y) anchor, Func<double, double, IEnumerable<Box>> candidates, string layer = Layers.Text)
    {
        var w = DrawPlan.TextWidth(text, h);
        var b = placer.First(candidates(w, h));
        var ex = Math.Clamp(anchor.X, b.X0, b.X1);
        var ey = anchor.Y < b.Y0 ? b.Y0 : anchor.Y > b.Y1 ? b.Y1 : b.Y0;
        d.Leader(layer, anchor, (ex, ey));
        d.Text(layer, text, b.X0, (b.Y0 + b.Y1) / 2, h, "middle_left");
        return b;
    }

    /// <summary>Balloon mark placed by the placer, with a leader to <paramref name="anchor"/>.</summary>
    public static Box Balloon(DrawPlan d, Placer placer, string mark, double th, (double X, double Y) anchor, Func<double, double, IEnumerable<Box>> candidates)
    {
        var r = Math.Max(th * 1.3, (DrawPlan.TextWidth(mark, th) / 2) + (th * 0.4));
        var b = placer.First(candidates(2 * r, 2 * r));
        Annotate.Mark(d, mark, anchor, ((b.X0 + b.X1) / 2, (b.Y0 + b.Y1) / 2), th);
        return b;
    }

    /// <summary>
    /// Weld symbol (reference line + basic symbol on the arrow side + size) with a leader to <paramref name="anchor"/>.
    /// Drawn as plain geometry tagged {kind: weld, block: HS_WELD_*} so a backend can replace it with the legacy block.
    /// </summary>
    public static Box Weld(DrawPlan d, Placer placer, WeldKind kind, double size, (double X, double Y) anchor, Func<double, double, IEnumerable<Box>> candidates, string? mark = null)
    {
        var k = d.Scale;
        double w = 13 * k, h = 4.6 * k;
        var b = placer.First(candidates(w, h));
        placer.Block(b);
        var saved = d.CurrentTag;
        var tag = saved?.DeepClone().AsObject() ?? [];
        tag["kind"] = "weld";
        tag["block"] = kind == WeldKind.Fillet ? FilletBlock : GrooveBlock;
        tag["size"] = size;
        if (mark is not null)
        {
            tag["part"] = mark;
        }

        d.CurrentTag = tag;
        var ry = b.Y0 + (3.6 * k);
        var right = anchor.X > (b.X0 + b.X1) / 2;
        var rx = right ? b.X1 : b.X0;
        d.Leader(Layers.Weld, anchor, (rx, ry));
        d.Line(Layers.Weld, b.X0, ry, b.X1, ry);
        var sx = b.X0 + (6 * k);
        var leg = 2.4 * k;
        if (kind == WeldKind.Fillet)
        {
            d.Polyline(Layers.Weld, false, (sx, ry), (sx, ry - leg), (sx + leg, ry));
        }
        else
        {
            d.Line(Layers.Weld, sx, ry, sx, ry - leg);
            d.Line(Layers.Weld, sx, ry, sx + leg, ry - leg);
        }

        d.Text(Layers.Weld, Annotate.F(size), sx - (0.8 * k), ry - (leg / 2), 2 * k, "middle_right");
        d.CurrentTag = saved;
        d.Meta["welds"] = (d.Meta["welds"]?.GetValue<int>() ?? 0) + 1;
        return b;
    }

    /// <summary>
    /// Enlarged cross-section with overall dimensions and a title. The section scale is the largest standard
    /// scale that keeps the section within <paramref name="maxPaper"/> mm (never smaller than the view scale).
    /// Drawn with its top-left corner at (x, top). Returns the occupied box.
    /// </summary>
    public static Box Section(DrawPlan d, Profile p, double x, double top, double maxPaper = 42)
    {
        var s = d.Scale;
        var secScale = Math.Min(s, SheetFrame.ScaleFor(Math.Max(p.Depth, p.Width), maxPaper));
        var u = s / secScale;
        double P(double v) => v * s;
        var th = P(Paper.Text);
        var title = $"SECTION  S=1/{Annotate.F(secScale)}";
        var dimL = P(10);
        double w = p.Width * u, h = p.Depth * u;
        var x0 = x + dimL;
        var y1 = top - P(6);
        var cx = x0 + (w / 2);
        var cy = y1 - (h / 2);
        d.Text(Layers.Text, title, x0, top - P(2.5), th * 0.9, "middle_left");
        MemberViews.Section(d, p, cx, cy, u);
        var c = P(2);
        d.Line(Layers.Center, cx, y1 + c, cx, y1 - h - c);
        d.Line(Layers.Center, x0 - c, cy, x0 + w + c, cy);
        var yb = y1 - h;
        d.Dim(Layers.Dim, (x0, yb), (x0 + w, yb), (x0, yb - P(6)), 0, Annotate.F(p.Width));
        d.Dim(Layers.Dim, (x0, yb), (x0, y1), (x0 - P(6), yb), 90, Annotate.F(p.Depth));
        var width = Math.Max(dimL + w + P(2), DrawPlan.TextWidth(title, th * 0.9) + dimL);
        d.Meta["section_scale"] = secScale;
        return new Box(x, yb - P(7), x + width, top);
    }

    /// <summary>Front-view point on an attachment where its weld callout points.</summary>
    public static (double X, double Y) FrontAnchor(Attachment at, Func<double, double> mapX, double frontY)
    {
        var o = at.At.Origin;
        var pl = at.Part;
        return at.At.Orientation switch
        {
            PlateOrientation.Web => (mapX(o.X + (pl.SizeU / 2)), frontY + o.Y + (pl.SizeV * 0.25)),
            PlateOrientation.Flange => (mapX(o.X + (pl.SizeU / 2)), frontY + o.Y + (at.At.NSign * pl.Thickness / 2)),
            _ => (mapX(o.X + (at.At.NSign * pl.Thickness / 2)), frontY + o.Y + (pl.SizeV * 0.25)),
        };
    }

    internal static JsonObject Tag(string kind, string mark) => new() { ["kind"] = kind, ["mark"] = mark };
}
