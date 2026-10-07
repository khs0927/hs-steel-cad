using System.Globalization;

namespace HsSteel.Drafting;

/// <summary>A dimension station: where it is drawn and the true position it stands for.</summary>
public readonly record struct Station(double Drawn, double True);

/// <summary>Dimension and table helpers shared by all generators. Sizes are paper mm × scale.</summary>
public static class Annotate
{
    /// <summary>
    /// Chain dimension through the stations. Equal consecutive spacings are merged into one dimension
    /// written as "n@p=total" (HS-STEEL style, e.g. "4@60=240"). Values are always true lengths.
    /// When neighbouring dimension texts would collide (narrow hole pitch at small scale) the row switches to
    /// ordinate (cumulative) dimensioning: ticks at every station on one baseline, each labelled with its true
    /// distance from the first station; labels too close together are spread apart and joined by jogged leaders.
    /// </summary>
    /// <param name="horizontal">True: stations are X values, measured from <paramref name="edge"/> (Y of the object) to the line at <paramref name="line"/>.</param>
    /// <returns>How far (model units) the row's annotations reach beyond <paramref name="line"/>, away from the object.</returns>
    public static double Chain(DrawPlan d, IReadOnlyList<Station> stations, bool horizontal, double edge, double line)
    {
        var dims = ChainDims(stations);
        if (dims.Count == 0)
        {
            return 0;
        }

        var sg = line >= edge ? 1 : -1;
        if (!Crowded(dims, horizontal, d.Scale))
        {
            foreach (var (a, b, text) in dims)
            {
                Dim(d, a, b, horizontal, edge, line, text);
            }

            return ChainBeyond(horizontal, sg, d.Scale);
        }

        return Ordinate(d, Sorted(stations), horizontal, edge, line);
    }

    /// <summary>Depth the row would need beyond its line, without drawing (for laying out rows in advance).</summary>
    public static double ChainDepth(IReadOnlyList<Station> stations, bool horizontal, double sign, double scale)
    {
        var dims = ChainDims(stations);
        if (dims.Count == 0)
        {
            return 0;
        }

        if (!Crowded(dims, horizontal, scale))
        {
            return ChainBeyond(horizontal, sign >= 0 ? 1 : -1, scale);
        }

        var h = AnnotationBoxes.DimTextHeight(scale);
        var s = Sorted(stations);
        return (3.5 * scale) + s.Max(v => DrawPlan.TextWidth(F(v.True - s[0].True), h));
    }

    /// <summary>Gap between this row's line and the next row's line (model units).</summary>
    public static double NextRow(double beyond, double scale) => Math.Max(Paper.DimRow * scale, beyond + (4.5 * scale));

    private static List<Station> Sorted(IReadOnlyList<Station> stations) =>
        stations.OrderBy(v => v.True).DistinctBy(v => Math.Round(v.True, 2)).ToList();

    private static List<(double A, double B, string Text)> ChainDims(IReadOnlyList<Station> stations)
    {
        var s = Sorted(stations);
        var dims = new List<(double, double, string)>();
        var i = 0;
        while (i + 1 < s.Count)
        {
            var len = s[i + 1].True - s[i].True;
            var j = i + 1;
            while (j + 1 < s.Count && Math.Abs((s[j + 1].True - s[j].True) - len) < 0.05)
            {
                j++;
            }

            var n = j - i;
            if (Math.Abs(s[j].Drawn - s[i].Drawn) > 1e-6)
            {
                dims.Add((s[i].Drawn, s[j].Drawn, n > 1 ? $"{n}@{F(len)}={F(len * n)}" : F(len)));
            }

            i = j;
        }

        return dims;
    }

    private static bool Crowded(List<(double A, double B, string Text)> dims, bool horizontal, double scale)
    {
        var boxes = dims.Select(x => AnnotationBoxes.DimText(!horizontal, 0, (x.A + x.B) / 2, x.Text, scale)).ToList();
        var pad = 0.6 * scale;
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                if (boxes[i].Overlaps(boxes[j], pad))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Dimension text sits above a horizontal line and left of a vertical one (backend convention).
    private static double ChainBeyond(bool horizontal, int sg, double scale) =>
        (horizontal ? sg > 0 : sg < 0) ? (0.4 + AnnotationBoxes.DimTextPaper + 0.4) * scale : 1.2 * scale;

    private static double Ordinate(DrawPlan d, List<Station> s, bool horizontal, double edge, double line)
    {
        var k = d.Scale;
        var sg = line >= edge ? 1 : -1;
        var h = AnnotationBoxes.DimTextHeight(k);
        var labels = s.Select(v => F(v.True - s[0].True)).ToList();
        var at = Spread([.. s.Select(v => v.Drawn)], h * 1.3);
        (double X, double Y) Pt(double along, double across) => horizontal ? (along, across) : (across, along);
        void Seg((double X, double Y) a, (double X, double Y) b) => d.Line(Layers.Dim, a.X, a.Y, b.X, b.Y);

        var gap = Math.Min(Math.Abs(line - edge) * 0.2, 1.2 * k);
        Seg(Pt(s[0].Drawn, line), Pt(s[^1].Drawn, line));
        var o = Pt(s[0].Drawn, line);
        d.Circle(Layers.Dim, o.X, o.Y, 0.7 * k);
        for (var i = 0; i < s.Count; i++)
        {
            var x = s[i].Drawn;
            Seg(Pt(x, edge + (sg * gap)), Pt(x, line + (sg * 1.2 * k)));
            if (Math.Abs(at[i] - x) > 1e-6)
            {
                Seg(Pt(x, line + (sg * 1.2 * k)), Pt(at[i], line + (sg * 2.6 * k)));
                Seg(Pt(at[i], line + (sg * 2.6 * k)), Pt(at[i], line + (sg * 3.1 * k)));
            }
            else
            {
                Seg(Pt(x, line + (sg * 1.2 * k)), Pt(x, line + (sg * 3.1 * k)));
            }

            var p = Pt(at[i], line + (sg * 3.5 * k));
            d.Text(Layers.Dim, labels[i], p.X, p.Y, h, sg > 0 ? "middle_left" : "middle_right", horizontal ? 90 : 0);
        }

        return (3.5 * k) + labels.Max(t => DrawPlan.TextWidth(t, h));
    }

    /// <summary>
    /// Spreads label positions so neighbours are at least <paramref name="gap"/> apart while staying as close as
    /// possible to their targets (each run of touching labels is centred on its targets). Deterministic.
    /// </summary>
    public static List<double> Spread(IReadOnlyList<double> targets, double gap)
    {
        // Cluster = run of labels spaced exactly gap; Adj = sum of (target - offset in run) so start = Adj / Count.
        var clusters = new List<(int Start, int Count, double Adj)>();
        for (var i = 0; i < targets.Count; i++)
        {
            clusters.Add((i, 1, targets[i]));
            while (clusters.Count > 1)
            {
                var a = clusters[^2];
                var b = clusters[^1];
                var aEnd = (a.Adj / a.Count) + (gap * (a.Count - 1));
                if ((b.Adj / b.Count) - aEnd >= gap - 1e-9)
                {
                    break;
                }

                clusters.RemoveAt(clusters.Count - 1);
                clusters[^1] = (a.Start, a.Count + b.Count, a.Adj + b.Adj - (b.Count * a.Count * gap));
            }
        }

        var res = new double[targets.Count];
        foreach (var (start, count, adj) in clusters)
        {
            for (var j = 0; j < count; j++)
            {
                res[start + j] = (adj / count) + (j * gap);
            }
        }

        return [.. res];
    }

    public static void Dim(DrawPlan d, double a, double b, bool horizontal, double edge, double line, string text)
    {
        if (Math.Abs(b - a) < 1e-6)
        {
            return;
        }

        if (horizontal)
        {
            d.Dim(Layers.Dim, (a, edge), (b, edge), (a, line), 0, text);
        }
        else
        {
            d.Dim(Layers.Dim, (edge, a), (edge, b), (line, a), 90, text);
        }
    }

    /// <summary>Balloon mark with a leader from <paramref name="target"/> to the label at <paramref name="at"/>.</summary>
    public static void Mark(DrawPlan d, string mark, (double X, double Y) target, (double X, double Y) at, double textH)
    {
        var r = Math.Max(textH * 1.3, DrawPlan.TextWidth(mark, textH) / 2 + (textH * 0.4));
        var dx = at.X - target.X;
        var dy = at.Y - target.Y;
        var len = Math.Sqrt((dx * dx) + (dy * dy));
        var end = len > r ? (at.X - (dx / len * r), at.Y - (dy / len * r)) : at;
        d.Leader(Layers.Mark, target, end);
        d.Circle(Layers.Mark, at.X, at.Y, r);
        d.Text(Layers.Mark, mark, at.X, at.Y, textH);
    }

    /// <summary>Grid bubble (circle with name) at the end of a grid line.</summary>
    public static void Bubble(DrawPlan d, string name, double x, double y, double r)
    {
        d.Circle(Layers.Grid, x, y, r);
        d.Text(Layers.Grid, name, x, y, r * 0.9);
    }

    /// <summary>Draws a table with its top-left corner at (x, y). Returns its height.</summary>
    public static double Table(DrawPlan d, double x, double y, IReadOnlyList<(string Header, double Width)> columns, IReadOnlyList<string[]> rows, double textH, string? title = null)
    {
        var rowH = textH * 2;
        var width = columns.Sum(c => c.Width);
        var top = y;
        if (title is not null)
        {
            d.Text(Layers.Table, title, x, top + (textH * 0.8), textH * 1.2, "bottom_left");
        }

        var all = new List<string[]> { columns.Select(c => c.Header).ToArray() };
        all.AddRange(rows);
        var height = rowH * all.Count;
        d.Rect(Layers.Table, x, top - height, width, height);
        for (var r = 0; r < all.Count; r++)
        {
            var yy = top - (rowH * (r + 1));
            if (r > 0)
            {
                d.Line(Layers.Table, x, yy + rowH, x + width, yy + rowH);
            }

            var cx = x;
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = c < all[r].Length ? all[r][c] : "";
                d.Text(Layers.Table, cell, cx + (columns[c].Width / 2), yy + (rowH / 2), textH);
                cx += columns[c].Width;
            }
        }

        var lx = x;
        foreach (var c in columns.Take(columns.Count - 1))
        {
            lx += c.Width;
            d.Line(Layers.Table, lx, top, lx, top - height);
        }

        d.Line(Layers.Table, x, top - rowH, x + width, top - rowH);
        return height;
    }

    public static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
