using System.Text.Json.Nodes;
using HsSteel.Domain;
using HsSteel.Modeling;

namespace HsSteel.Drafting;

/// <summary>Erection plan (설치도): grids with bubbles and dimensions, members with assembly marks, per level.</summary>
public static class LayoutPlan
{
    public static IEnumerable<DrawPlan> Generate(ModelResult model, SheetFrame frame)
    {
        var project = model.Project;

        // Only members the model builder accepted (it skips duplicates, zero-length axes and unknown
        // sections with a warning); anything else has no profile and no mark.
        var built = project.Members.Where(m => model.Profiles.ContainsKey(m.Id)).DistinctBy(m => m.Id).ToList();
        var horizontal = built.Where(m => Math.Abs((m.End - m.Start).Unit.Z) < 0.2).ToList();
        var columns = built.Where(m => Math.Abs((m.End - m.Start).Unit.Z) >= 0.2).ToList();
        var levels = horizontal.GroupBy(m => Math.Round((m.Start.Z + m.End.Z) / 2 / 10) * 10).OrderBy(g => g.Key).ToList();
        if (levels.Count == 0 && columns.Count > 0)
        {
            levels = columns.GroupBy(_ => 0.0).ToList();
            horizontal = [];
        }

        foreach (var level in levels)
        {
            var z = level.Key;
            var members = level.Where(m => horizontal.Contains(m)).ToList();
            var cols = columns.Where(c => Math.Min(c.Start.Z, c.End.Z) <= z + 1 && Math.Max(c.Start.Z, c.End.Z) >= z - 1).ToList();
            yield return Plan(model, frame, $"EL. {(z >= 0 ? "+" : "")}{Annotate.F(z)}", members, cols);
        }
    }

    private static DrawPlan Plan(ModelResult model, SheetFrame frame, string title, List<MemberDef> members, List<MemberDef> cols)
    {
        var project = model.Project;
        var xs = members.SelectMany(m => new[] { m.Start.X, m.End.X }).Concat(cols.Select(c => c.Start.X)).Concat(project.GridX.Select(g => g.Position)).ToList();
        var ys = members.SelectMany(m => new[] { m.Start.Y, m.End.Y }).Concat(cols.Select(c => c.Start.Y)).Concat(project.GridY.Select(g => g.Position)).ToList();
        double minX = xs.DefaultIfEmpty(0).Min(), maxX = xs.DefaultIfEmpty(1000).Max(), minY = ys.DefaultIfEmpty(0).Min(), maxY = ys.DefaultIfEmpty(1000).Max();
        var s = frame.FitScale(Math.Max(maxX - minX, 1000) * 1.35, Math.Max(maxY - minY, 1000) * 1.45);
        double P(double v) => v * s;
        var th = P(Paper.Text);
        var d = new DrawPlan { Title = title, Scale = s };
        var ext = P(14);
        var r = P(4);

        foreach (var g in project.GridX.OrderBy(g => g.Position))
        {
            d.Line(Layers.Grid, g.Position, minY - ext, g.Position, maxY + ext);
            Annotate.Bubble(d, g.Name, g.Position, maxY + ext + r, r);
            Annotate.Bubble(d, g.Name, g.Position, minY - ext - r, r);
        }

        foreach (var g in project.GridY.OrderBy(g => g.Position))
        {
            d.Line(Layers.Grid, minX - ext, g.Position, maxX + ext, g.Position);
            Annotate.Bubble(d, g.Name, minX - ext - r, g.Position, r);
            Annotate.Bubble(d, g.Name, maxX + ext + r, g.Position, r);
        }

        if (project.GridX.Count > 1)
        {
            var st = project.GridX.Select(g => g.Position).Order().Select(v => new Station(v, v)).ToList();
            Annotate.Chain(d, st, true, maxY + ext + (2 * r), maxY + ext + (2 * r) + P(6));
            Annotate.Dim(d, st[0].Drawn, st[^1].Drawn, true, maxY + ext + (2 * r), maxY + ext + (2 * r) + P(12), Annotate.F(st[^1].True - st[0].True));
        }

        if (project.GridY.Count > 1)
        {
            var st = project.GridY.Select(g => g.Position).Order().Select(v => new Station(v, v)).ToList();
            Annotate.Chain(d, st, false, minX - ext - (2 * r), minX - ext - (2 * r) - P(6));
            Annotate.Dim(d, st[0].Drawn, st[^1].Drawn, false, minX - ext - (2 * r), minX - ext - (2 * r) - P(12), Annotate.F(st[^1].True - st[0].True));
        }

        foreach (var m in members)
        {
            var mark = model.MemberMarks.GetValueOrDefault(m.Id, m.Id);
            d.CurrentTag = new JsonObject { ["kind"] = "member", ["id"] = m.Id, ["mark"] = mark };
            d.Line(Layers.Member, m.Start.X, m.Start.Y, m.End.X, m.End.Y);
            var ang = Math.Atan2(m.End.Y - m.Start.Y, m.End.X - m.Start.X) * 180 / Math.PI;
            if (ang > 90.001 || ang <= -90)
            {
                ang += ang > 0 ? -180 : 180;
            }

            var mx = (m.Start.X + m.End.X) / 2;
            var my = (m.Start.Y + m.End.Y) / 2;
            var nx = -Math.Sin(ang * Math.PI / 180) * P(2.5);
            var ny = Math.Cos(ang * Math.PI / 180) * P(2.5);
            d.Text(Layers.Mark, mark, mx + nx, my + ny, th, "bottom_center", Math.Round(ang, 3));
            d.Text(Layers.Text, m.Section, mx - nx, my - ny, th * 0.7, "top_center", Math.Round(ang, 3));
        }

        foreach (var c in cols)
        {
            var mark = model.MemberMarks.GetValueOrDefault(c.Id, c.Id);
            d.CurrentTag = new JsonObject { ["kind"] = "member", ["id"] = c.Id, ["mark"] = mark };
            var prof = model.Profiles[c.Id];
            var f = MemberFrame.Of(c);
            double hw = prof.Width / 2, hd = prof.Depth / 2;
            (double, double) Corner(double a, double b) => (c.Start.X + (f.Y.X * a) + (f.Z.X * b), c.Start.Y + (f.Y.Y * a) + (f.Z.Y * b));
            d.Polyline(Layers.Member, true, Corner(-hd, -hw), Corner(hd, -hw), Corner(hd, hw), Corner(-hd, hw));
            d.Text(Layers.Mark, mark, c.Start.X + P(3), c.Start.Y + P(3), th, "bottom_left");
        }

        d.CurrentTag = null;
        d.Text(Layers.Text, "PLAN  " + title, minX - ext, minY - ext - (2 * r) - P(12), th * 1.6, "middle_left");
        d.Meta["kind"] = "plan";
        return d;
    }
}

/// <summary>Bill of materials sheets: assembly list and material summary (자재집계표).</summary>
public static class BomSheets
{
    public static IEnumerable<DrawPlan> Generate(ModelResult model, SheetFrame frame)
    {
        const double th = 2.5;
        var rowH = th * 2;
        var maxRows = (int)((frame.AreaH - 20) / rowH) - 2;

        var asm = model.Assemblies.OrderBy(a => a.Type).ThenBy(a => a.Mark, StringComparer.Ordinal)
            .Select(a => new[] { a.Mark, a.Type.ToString().ToUpperInvariant(), a.Main.Profile.Spec, Annotate.F(a.Main.Length), $"{a.Quantity}", $"{a.Weight:0.0}", $"{a.Weight * a.Quantity:0.0}" })
            .ToList();
        var total = model.Assemblies.Sum(a => a.Weight * a.Quantity);
        asm.Add(["", "TOTAL", "", "", $"{model.Assemblies.Sum(a => a.Quantity)}", "", $"{total:0.0}"]);
        (string, double)[] asmCols = [("ASSY", 20), ("TYPE", 26), ("MAIN MEMBER", 46), ("LENGTH", 20), ("Q'TY", 14), ("UNIT(kg)", 22), ("TOTAL(kg)", 24)];
        foreach (var d in Tables("ASSEMBLY LIST", asmCols, asm, maxRows, th))
        {
            yield return d;
        }

        var mat = new List<string[]>();
        foreach (var g in model.ShapeParts.GroupBy(p => p.Profile.Spec).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var len = g.Sum(p => p.Length * p.Quantity) / 1000;
            var wt = g.Sum(p => p.Weight * p.Quantity);
            var paint = g.Sum(p => p.PaintArea * p.Quantity);
            mat.Add([g.Key, g.First().Profile.Family, $"{len:0.00}", $"{g.First().Profile.UnitWeight:0.##}", $"{wt:0.0}", $"{paint:0.00}"]);
        }

        foreach (var g in model.PlateParts.GroupBy(p => p.Thickness).OrderBy(g => g.Key))
        {
            var area = g.Sum(p => p.SizeU * p.SizeV * p.Quantity) / 1e6;
            var wt = g.Sum(p => p.Weight * p.Quantity);
            mat.Add([$"PL-{Annotate.F(g.Key)}", "PLATE", $"{area:0.00} m²", "", $"{wt:0.0}", $"{area * 2:0.00}"]);
        }

        var bolts = model.Assemblies.SelectMany(a => a.Bolts.Select(b => (b.Name, Count: b.Count * a.Quantity))).GroupBy(b => b.Name);
        foreach (var b in bolts.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            mat.Add([b.Key, "BOLT", $"{b.Sum(x => x.Count)} EA", "", "", ""]);
        }

        mat.Add(["TOTAL", "", "", "", $"{model.ShapeParts.Sum(p => p.Weight * p.Quantity) + model.PlateParts.Sum(p => p.Weight * p.Quantity):0.0}", ""]);
        (string, double)[] matCols = [("SPEC", 50), ("KIND", 30), ("LENGTH(m)/AREA", 34), ("kg/m", 18), ("WEIGHT(kg)", 26), ("PAINT(m²)", 24)];
        foreach (var d in Tables("MATERIAL SUMMARY", matCols, mat, maxRows, th))
        {
            yield return d;
        }
    }

    private static IEnumerable<DrawPlan> Tables(string title, (string, double)[] cols, List<string[]> rows, int maxRows, double th)
    {
        for (var i = 0; i < rows.Count; i += maxRows)
        {
            var chunk = rows.Skip(i).Take(maxRows).ToList();
            var d = new DrawPlan { Title = title, Scale = 1 };
            var h = (chunk.Count + 1) * th * 2;
            Annotate.Table(d, 0, h, cols, chunk, th, i == 0 ? title : title + " (CONT.)");
            d.Meta["kind"] = "bom";
            yield return d;
        }
    }
}

/// <summary>A composed sheet: frame + packed views, ready for a backend.</summary>
public sealed record Sheet(string Number, string Title, double Scale, DrawPlan Plan, double OriginX, double OriginY);

/// <summary>
/// Packs generated views into the company frame. Views of the same scale share sheets (shelf packing in
/// paper space); a view larger than the drawing area gets a sheet of its own. Sheets are laid out side by
/// side in model space, each frame inserted at its sheet scale with title fields filled in.
/// </summary>
public sealed class SheetComposer(SheetFrame frame, string project, string date = "")
{
    private double _cursorX;

    public List<string> Warnings { get; } = [];

    public IReadOnlyList<Sheet> Compose(string prefix, string title, IEnumerable<DrawPlan> views)
    {
        var sheets = new List<Sheet>();
        var gap = 8.0; // paper mm between views
        foreach (var group in views.GroupBy(v => v.Scale).OrderBy(g => g.Key))
        {
            var scale = group.Key;
            List<(DrawPlan View, double X, double Y)> placed = [];
            double rowY = frame.AreaH, rowX = 0, rowH = 0;
            foreach (var v in group)
            {
                var (x0, y0, x1, y1) = v.Extents();
                double w = (x1 - x0) / scale, h = (y1 - y0) / scale;
                if (w > frame.AreaW || h > frame.AreaH)
                {
                    Warnings.Add($"{v.Title}: {w:0}x{h:0} mm exceeds the drawing area at 1/{scale}.");
                }

                if (rowX > 0 && rowX + w > frame.AreaW)
                {
                    rowY -= rowH + gap;
                    rowX = 0;
                    rowH = 0;
                }

                if (placed.Count > 0 && rowY - h < 0)
                {
                    sheets.Add(Flush(prefix, title, scale, placed, sheets.Count + 1));
                    placed = [];
                    rowY = frame.AreaH;
                    rowX = 0;
                    rowH = 0;
                }

                placed.Add((v, rowX - (x0 / scale), rowY - h - (y0 / scale)));
                rowX += w + gap;
                rowH = Math.Max(rowH, h);
            }

            if (placed.Count > 0)
            {
                sheets.Add(Flush(prefix, title, scale, placed, sheets.Count + 1));
            }
        }

        return sheets;
    }

    private Sheet Flush(string prefix, string title, double scale, List<(DrawPlan View, double X, double Y)> placed, int n)
    {
        var number = $"{prefix}-{n:000}";
        var ox = _cursorX;
        var oy = 0.0;
        var d = new DrawPlan { Title = $"{number} {title}", Scale = scale };
        foreach (var (view, x, y) in placed)
        {
            d.Append(view, ox + ((frame.AreaX + x) * scale), oy + ((frame.AreaY + y) * scale));
        }

        var fields = new Dictionary<string, string>
        {
            ["project"] = project,
            ["title"] = title + (placed.Count == 1 ? " " + placed[0].View.Title : ""),
            ["dwg_no"] = number,
            ["scale"] = $"1/{Annotate.F(scale)}",
            ["date"] = date,
        };
        JsonObject? attrs = null;
        if (frame.AttributeTags.Count > 0)
        {
            attrs = [];
            foreach (var (field, tag) in frame.AttributeTags)
            {
                attrs[tag] = fields.GetValueOrDefault(field, "");
            }
        }

        d.CurrentTag = new JsonObject { ["kind"] = "sheet", ["dwg_no"] = number };
        d.Insert(Layers.Frame, frame.BlockName, ox + (frame.BaseX * scale), oy + (frame.BaseY * scale), scale, attrs);
        if (frame.AttributeTags.Count == 0)
        {
            foreach (var (field, slot) in frame.TextSlots)
            {
                d.Text(Layers.Frame, fields.GetValueOrDefault(field, ""), ox + (slot.X * scale), oy + (slot.Y * scale), slot.Height * scale, slot.Justify);
            }
        }

        d.CurrentTag = null;
        d.Meta["dwg_no"] = number;
        d.Meta["views"] = new JsonArray([.. placed.Select(p => (JsonNode)p.View.Title)]);
        _cursorX += (frame.Width + 30) * scale;
        return new Sheet(number, title, scale, d, ox, oy);
    }
}

/// <summary>Everything generated for a project.</summary>
public sealed class DrawingSet
{
    public required ModelResult Model { get; init; }

    public List<Sheet> Sheets { get; } = [];

    public List<string> Warnings { get; } = [];

    /// <summary>Which drawing kinds to generate.</summary>
    [Flags]
    public enum Kinds { Assembly = 1, Part = 2, Plate = 4, Plan = 8, Bom = 16, All = 31 }

    public static DrawingSet Generate(Project project, SectionCatalog catalog, SpliceStandards splices, SheetFrame frame, Kinds kinds = Kinds.All)
    {
        var model = new ModelBuilder(catalog, splices).Build(project);
        var set = new DrawingSet { Model = model };
        set.Warnings.AddRange(model.Warnings);
        var composer = new SheetComposer(frame, project.Name, project.Date);
        if (kinds.HasFlag(Kinds.Plan))
        {
            set.Sheets.AddRange(composer.Compose("E", "ERECTION PLAN", LayoutPlan.Generate(model, frame)));
        }

        if (kinds.HasFlag(Kinds.Assembly))
        {
            set.Sheets.AddRange(composer.Compose("A", "ASSY DWG", model.Assemblies.OrderBy(a => a.Type).ThenBy(a => a.Mark, StringComparer.Ordinal).Select(x => AssemblyDetail.Generate(x, frame.AreaW - 10))));
        }

        if (kinds.HasFlag(Kinds.Part))
        {
            set.Sheets.AddRange(composer.Compose("S", "SHAPE DETAIL", model.ShapeParts.Select(PartDetail.Generate)));
        }

        if (kinds.HasFlag(Kinds.Plate))
        {
            set.Sheets.AddRange(composer.Compose("P", "PLATE DETAIL", model.PlateParts.Select(PlateDetail.Generate)));
        }

        if (kinds.HasFlag(Kinds.Bom))
        {
            set.Sheets.AddRange(composer.Compose("M", "BILL OF MATERIALS", BomSheets.Generate(model, frame)));
        }

        set.Warnings.AddRange(composer.Warnings);
        return set;
    }
}
