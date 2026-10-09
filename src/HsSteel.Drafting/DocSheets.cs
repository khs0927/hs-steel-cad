using HsSteel.Modeling;

namespace HsSteel.Drafting;

/// <summary>
/// Extents of an original HS-STEEL block (C:\HS-STEEL\HSSTEEL\block\*.dwg) in block units, i.e. relative to the
/// file's $INSBASE, measured from the ODA-converted DXF. The originals are not drawn from their lower-left corner,
/// so a block is inserted at <c>target - (MinX, MinY)</c> and the sheet scale is chosen from its size.
/// </summary>
public sealed record BlockExtents(double MinX, double MinY, double W, double H);

public static class HsBlocks
{
    public const string Notes = "철골_시공상세도_일반사항-100";
    public const string FilletWeld = "FILLET_WELD_TABLE";
    public const string BracketWeld = "BRACKET_WELD_TABLE";
    public const string BoltData = "BOLT-DATA-TABLE";
    public const string HoleDia = "HOLE-DIA-TABLE";

    /// <summary>Measured 2026-10-09 with ezdxf from D:\PowerCad-Assets-staging\dxf\HSSTEEL\block, minus $INSBASE.</summary>
    public static readonly IReadOnlyDictionary<string, BlockExtents> Extents = new Dictionary<string, BlockExtents>
    {
        [Notes] = new(0, -6552, 43128, 35052),
        [FilletWeld] = new(-72, 0, 72, 35),
        [BracketWeld] = new(-72, 0, 72, 21),
        [BoltData] = new(0, -11500, 5765, 13275),
        [HoleDia] = new(-578, -4556, 13014, 5156),
        ["HAS M16x190K"] = new(-16, -152, 32, 190),
        ["HAS M20x240K"] = new(-20, -190, 40, 240),
        ["HAS M24x290K"] = new(-24, -218, 48, 290),
    };

    /// <summary>HAS anchor block for an anchor hole diameter (hole = bolt + 3, nearest of M16/M20/M24).</summary>
    public static string Anchor(double holeDia)
    {
        var bolt = holeDia - 3;
        return bolt <= 18 ? "HAS M16x190K" : bolt <= 22 ? "HAS M20x240K" : "HAS M24x290K";
    }

    /// <summary>
    /// A view holding one original block with its lower-left corner at (0, 0), framed by a rectangle on HS-TABLE
    /// (the composer measures views by their non-insert entities). The view scale fits the block in the drawing area.
    /// </summary>
    public static DrawPlan View(string block, string title, SheetFrame frame)
    {
        var e = Extents[block];
        var d = new DrawPlan { Title = title, Scale = frame.FitScale(e.W * 1.04, e.H * 1.04) };
        d.CurrentTag = new() { ["kind"] = "doc_block", ["block"] = block };
        d.Insert(Layers.Table, block, -e.MinX, -e.MinY, 1);
        d.Rect(Layers.Table, 0, 0, e.W, e.H);
        d.CurrentTag = null;
        return d;
    }
}

/// <summary>표지 (G-001): project, date and the drawing index of every generated sheet.</summary>
public static class CoverSheet
{
    public static DrawPlan Generate(string project, string date, IReadOnlyList<(string No, string Title, string Scale)> index, SheetFrame frame)
    {
        const double th = 3;
        var d = new DrawPlan { Title = "표지 / DRAWING LIST", Scale = 1 };
        d.CurrentTag = new() { ["kind"] = "cover" };
        var top = frame.AreaH - 10;
        d.Text(Layers.Text, "STEEL SHOP DRAWING", frame.AreaW / 2, top, 10);
        d.Text(Layers.Text, "철골 제작도", frame.AreaW / 2, top - 16, 8);
        d.Text(Layers.Text, project, frame.AreaW / 2, top - 32, 7);
        if (date.Length > 0)
        {
            d.Text(Layers.Text, date, frame.AreaW / 2, top - 44, 4);
        }

        (string, double)[] cols = [("DWG NO.", 30), ("TITLE", 90), ("SCALE", 25)];
        var perColumn = (int)Math.Max(1, ((top - 60) / (th * 2)) - 2);
        var x = 10.0;
        for (var i = 0; i < index.Count; i += perColumn)
        {
            var rows = index.Skip(i).Take(perColumn).Select(r => new[] { r.No, r.Title, r.Scale }).ToList();
            Annotate.Table(d, x, top - 60, cols, rows, th, i == 0 ? "DRAWING LIST / 도면목록" : null);
            x += cols.Sum(c => c.Item2) + 10;
        }

        d.CurrentTag = null;
        return d;
    }
}

/// <summary>
/// 앵커 배치도 (F-001): grids, every column base plate at its grid position with its anchor holes, plate size and
/// anchor dimensions, an anchor schedule and the HAS anchor block as legend. Plates are drawn unrotated
/// (u along X = flange width, v along Y = depth), as the model builder places them.
/// </summary>
public static class AnchorPlan
{
    public static IEnumerable<DrawPlan> Generate(ModelResult model, SheetFrame frame)
    {
        var project = model.Project;
        var bases = project.Connections.OfType<BasePlateDef>().ToList();
        var cols = project.Members.Where(m => model.Profiles.ContainsKey(m.Id)).DistinctBy(m => m.Id).ToDictionary(m => m.Id);
        var items = bases.Where(b => cols.ContainsKey(b.Column)).Select(b =>
        {
            var c = cols[b.Column];
            var p = model.Profiles[b.Column];
            var bottom = c.Start.Z <= c.End.Z ? c.Start : c.End;
            return (Def: b, X: bottom.X, Y: bottom.Y, U: p.Width + (2 * b.Margin), V: p.Depth + (2 * b.Margin), Mark: model.MemberMarks.GetValueOrDefault(b.Column, b.Column));
        }).ToList();
        if (items.Count == 0)
        {
            yield break;
        }

        var xs = items.Select(i => i.X).Concat(project.GridX.Select(g => g.Position)).ToList();
        var ys = items.Select(i => i.Y).Concat(project.GridY.Select(g => g.Position)).ToList();
        double minX = xs.Min(), maxX = xs.Max(), minY = ys.Min(), maxY = ys.Max();
        var s = frame.FitScale(Math.Max(maxX - minX, 1000) * 1.35, Math.Max(maxY - minY, 1000) * 1.6);
        double P(double v) => v * s;
        var d = new DrawPlan { Title = "ANCHOR PLAN", Scale = s };
        var ext = P(14);
        var r = P(4);
        foreach (var g in project.GridX)
        {
            d.Line(Layers.Grid, g.Position, minY - ext, g.Position, maxY + ext);
            Annotate.Bubble(d, g.Name, g.Position, maxY + ext + r, r);
        }

        foreach (var g in project.GridY)
        {
            d.Line(Layers.Grid, minX - ext, g.Position, maxX + ext, g.Position);
            Annotate.Bubble(d, g.Name, minX - ext - r, g.Position, r);
        }

        foreach (var it in items)
        {
            d.CurrentTag = new() { ["kind"] = "anchor", ["column"] = it.Def.Column, ["mark"] = it.Mark };
            double x0 = it.X - (it.U / 2), y0 = it.Y - (it.V / 2), e = it.Def.AnchorEdge;
            d.Rect(Layers.Plate, x0, y0, it.U, it.V);
            foreach (var (hx, hy) in new[] { (x0 + e, y0 + e), (x0 + it.U - e, y0 + e), (x0 + e, y0 + it.V - e), (x0 + it.U - e, y0 + it.V - e) })
            {
                d.Circle(Layers.Hole, hx, hy, it.Def.AnchorDia / 2);
                d.Line(Layers.Center, hx - (it.Def.AnchorDia * 0.8), hy, hx + (it.Def.AnchorDia * 0.8), hy);
                d.Line(Layers.Center, hx, hy - (it.Def.AnchorDia * 0.8), hx, hy + (it.Def.AnchorDia * 0.8));
            }

            d.Text(Layers.Mark, $"{it.Mark} BP{Annotate.F(it.Def.Thickness)}x{Annotate.F(it.U)}x{Annotate.F(it.V)}", it.X, y0 - P(4), P(Paper.Text));
        }

        d.CurrentTag = null;
        yield return d;

        // Schedule + legend: one row per anchor size, HAS block beside it.
        var groups = items.GroupBy(i => (Dia: i.Def.AnchorDia, Block: HsBlocks.Anchor(i.Def.AnchorDia))).OrderBy(g => g.Key.Dia).ToList();
        var t = new DrawPlan { Title = "ANCHOR SCHEDULE", Scale = 5 };
        var th = 2.5 * t.Scale;
        (string, double)[] cols2 = [("ANCHOR", 45 * t.Scale), ("HOLE", 18 * t.Scale), ("PLATES", 18 * t.Scale), ("QTY", 18 * t.Scale)];
        var rows = groups.Select(g => new[] { g.Key.Block, $"Ø{Annotate.F(g.Key.Dia)}", $"{g.Count()}", $"{g.Count() * 4}" }).ToList();
        rows.Add(["합계", "", $"{items.Count}", $"{items.Count * 4}"]);
        var h = Annotate.Table(t, 0, 0, cols2, rows, th, "ANCHOR SCHEDULE / 앵커 집계");
        var lx = cols2.Sum(c => c.Item2) + (10 * t.Scale);
        foreach (var g in groups)
        {
            var be = HsBlocks.Extents[g.Key.Block];
            t.CurrentTag = new() { ["kind"] = "doc_block", ["block"] = g.Key.Block };
            t.Insert(Layers.Table, g.Key.Block, lx - be.MinX, -h - be.MinY, 1);
            t.Rect(Layers.Table, lx, -h, be.W, be.H);
            t.Text(Layers.Text, g.Key.Block, lx + (be.W / 2), -h - (4 * t.Scale), th);
            t.CurrentTag = null;
            lx += be.W + (15 * t.Scale);
        }

        yield return t;
    }
}
