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
    /// </summary>
    /// <param name="horizontal">True: stations are X values, measured from <paramref name="edge"/> (Y of the object) to the line at <paramref name="line"/>.</param>
    public static void Chain(DrawPlan d, IReadOnlyList<Station> stations, bool horizontal, double edge, double line)
    {
        var s = stations.OrderBy(v => v.True).DistinctBy(v => Math.Round(v.True, 2)).ToList();
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
            var text = n > 1 ? $"{n}@{F(len)}={F(len * n)}" : F(len);
            Dim(d, s[i].Drawn, s[j].Drawn, horizontal, edge, line, text);
            i = j;
        }
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
