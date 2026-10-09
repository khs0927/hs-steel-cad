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


/// <summary>
/// Erection elevation (입면도): grid-line cuts showing columns and framing members in the YZ or XZ plane.
/// Generated together with <see cref="LayoutPlan"/> under DrawingSet.Kinds.Plan.
/// </summary>
public static class LayoutElevation
{
    public static IEnumerable<DrawPlan> Generate(ModelResult model, SheetFrame frame)
    {
        var project = model.Project;
        var built = project.Members.Where(m => model.Profiles.ContainsKey(m.Id)).DistinctBy(m => m.Id).ToList();
        if (built.Count == 0 || project.Levels.Count == 0)
        {
            yield break;
        }

        foreach (var gx in project.GridX.OrderBy(g => g.Position))
        {
            var cols = built.Where(m => IsColumn(m) && Near(m.Start.X, gx.Position) && Near(m.End.X, gx.Position)).ToList();
            var beams = built.Where(m => !IsColumn(m) && Near((m.Start.X + m.End.X) / 2, gx.Position) && Math.Abs(m.Start.Y - m.End.Y) > 1).ToList();
            if (cols.Count + beams.Count == 0)
            {
                continue;
            }

            yield return Elevation(model, frame, $"EL. GRID {gx.Name} (Y-Z)", alongY: true, cols, beams, project.GridY.Select(g => (g.Name, g.Position)).ToList());
        }

        foreach (var gy in project.GridY.OrderBy(g => g.Position))
        {
            var cols = built.Where(m => IsColumn(m) && Near(m.Start.Y, gy.Position) && Near(m.End.Y, gy.Position)).ToList();
            var beams = built.Where(m => !IsColumn(m) && Near((m.Start.Y + m.End.Y) / 2, gy.Position) && Math.Abs(m.Start.X - m.End.X) > 1).ToList();
            if (cols.Count + beams.Count == 0)
            {
                continue;
            }

            yield return Elevation(model, frame, $"EL. GRID {gy.Name} (X-Z)", alongY: false, cols, beams, project.GridX.Select(g => (g.Name, g.Position)).ToList());
        }
    }

    private static bool IsColumn(MemberDef m) => Math.Abs((m.End - m.Start).Unit.Z) >= 0.2;

    private static bool Near(double a, double b, double tol = 50) => Math.Abs(a - b) <= tol;

    private static DrawPlan Elevation(ModelResult model, SheetFrame frame, string title, bool alongY, List<MemberDef> cols, List<MemberDef> beams, List<(string Name, double Position)> grids)
    {
        double H(MemberDef m) => alongY ? m.Start.Y : m.Start.X;
        double H2(MemberDef m) => alongY ? m.End.Y : m.End.X;
        var hs = cols.Select(H).Concat(beams.SelectMany(m => new[] { H(m), H2(m) })).Concat(grids.Select(g => g.Position)).ToList();
        var zs = cols.SelectMany(m => new[] { m.Start.Z, m.End.Z }).Concat(beams.Select(m => (m.Start.Z + m.End.Z) / 2)).Concat(model.Project.Levels.Select(l => l.Elevation)).ToList();
        double minH = hs.DefaultIfEmpty(0).Min(), maxH = hs.DefaultIfEmpty(1000).Max();
        double minZ = zs.DefaultIfEmpty(0).Min(), maxZ = zs.DefaultIfEmpty(1000).Max();
        var s = frame.FitScale(Math.Max(maxH - minH, 1000) * 1.35, Math.Max(maxZ - minZ, 1000) * 1.35);
        double P(double v) => v * s;
        var th = P(Paper.Text);
        var d = new DrawPlan { Title = title, Scale = s };
        var ext = P(14);
        var r = P(4);

        foreach (var lv in model.Project.Levels.OrderBy(l => l.Elevation))
        {
            d.Line(Layers.Grid, minH - ext, lv.Elevation, maxH + ext, lv.Elevation);
            d.Text(Layers.Text, lv.Name, minH - ext - P(2), lv.Elevation, th * 0.85, "middle_right");
        }

        foreach (var g in grids.OrderBy(g => g.Position))
        {
            d.Line(Layers.Grid, g.Position, minZ - ext, g.Position, maxZ + ext);
            Annotate.Bubble(d, g.Name, g.Position, maxZ + ext + r, r);
            Annotate.Bubble(d, g.Name, g.Position, minZ - ext - r, r);
        }

        if (grids.Count > 1)
        {
            var st = grids.Select(g => g.Position).Order().Select(v => new Station(v, v)).ToList();
            Annotate.Chain(d, st, true, maxZ + ext + (2 * r), maxZ + ext + (2 * r) + P(6));
            Annotate.Dim(d, st[0].Drawn, st[^1].Drawn, true, maxZ + ext + (2 * r), maxZ + ext + (2 * r) + P(12), Annotate.F(st[^1].True - st[0].True));
        }

        if (model.Project.Levels.Count > 1)
        {
            var st = model.Project.Levels.Select(l => l.Elevation).Order().Select(v => new Station(v, v)).ToList();
            Annotate.Chain(d, st, false, minH - ext - (2 * r), minH - ext - (2 * r) - P(6));
        }

        foreach (var c in cols)
        {
            var mark = model.MemberMarks.GetValueOrDefault(c.Id, c.Id);
            d.CurrentTag = new System.Text.Json.Nodes.JsonObject { ["kind"] = "member", ["id"] = c.Id, ["mark"] = mark };
            var h = H(c);
            var prof = model.Profiles[c.Id];
            var half = prof.Depth / 2;
            d.Polyline(Layers.Member, true, (h - half, c.Start.Z), (h + half, c.Start.Z), (h + half, c.End.Z), (h - half, c.End.Z));
            d.Text(Layers.Mark, mark, h + half + P(2), (c.Start.Z + c.End.Z) / 2, th, "middle_left");
        }

        foreach (var m in beams)
        {
            var mark = model.MemberMarks.GetValueOrDefault(m.Id, m.Id);
            d.CurrentTag = new System.Text.Json.Nodes.JsonObject { ["kind"] = "member", ["id"] = m.Id, ["mark"] = mark };
            var z = (m.Start.Z + m.End.Z) / 2;
            var prof = model.Profiles[m.Id];
            var half = prof.Depth / 2;
            d.Line(Layers.Member, H(m), z, H2(m), z);
            d.Line(Layers.Member, H(m), z - half, H(m), z + half);
            d.Line(Layers.Member, H2(m), z - half, H2(m), z + half);
            var mx = (H(m) + H2(m)) / 2;
            d.Text(Layers.Mark, mark, mx, z + half + P(2), th, "bottom_center");
            d.Text(Layers.Text, m.Section, mx, z - half - P(2), th * 0.7, "top_center");
        }

        d.CurrentTag = null;
        d.Text(Layers.Text, "ELEVATION  " + title, minH - ext, minZ - ext - (2 * r) - P(12), th * 1.6, "middle_left");
        d.Meta["kind"] = "elevation";
        return d;
    }
}

/// <summary>Bill of materials sheets: 조립목록, 자재집계표, 볼트집계표 (drawing tables + <see cref="BomTable"/>).</summary>
public static class BomSheets
{
    public static IEnumerable<DrawPlan> Generate(ModelResult model, SheetFrame frame)
    {
        const double th = 2.5;
        var maxRows = (int)((frame.AreaH - 20) / (th * 2)) - 2;
        var bom = BomTable.From(model);

        var asm = bom.Assemblies
            .Select(a => new[] { a.Mark, a.Type, a.MainSpec, Annotate.F(a.Length), $"{a.Qty}", $"{a.UnitKg:0.0}", $"{a.TotalKg:0.0}" })
            .ToList();
        asm.Add(["", "합계", "", "", $"{bom.AssemblyQty}", "", $"{bom.TotalKg:0.0}"]);
        (string, double)[] asmCols = [("마크", 20), ("종류", 26), ("주부재", 46), ("길이", 20), ("수량", 14), ("단중(kg)", 22), ("중량(kg)", 24)];
        foreach (var d in Tables("조립목록 (ASSEMBLY LIST)", asmCols, asm, maxRows, th))
        {
            yield return d;
        }

        var mat = bom.Materials.Where(m => m.Kind != "BOLT")
            .Select(m => new[] { m.Spec, m.Kind, m.Quantity, m.UnitWeight > 0 ? $"{m.UnitWeight:0.##}" : "", $"{m.WeightKg:0.0}", $"{m.PaintM2:0.00}" })
            .ToList();
        mat.Add(["합계", "", "", "", $"{bom.TotalKg:0.0}", ""]);
        (string, double)[] matCols = [("규격", 50), ("종류", 30), ("수량", 34), ("단중", 18), ("중량(kg)", 26), ("도장(m²)", 24)];
        foreach (var d in Tables("자재집계표 (MATERIAL SUMMARY)", matCols, mat, maxRows, th))
        {
            yield return d;
        }

        var bolts = bom.Bolts.Select(b => new[] { b.Name, $"{b.Qty}", $"{b.Assemblies}", "" }).ToList();
        if (bolts.Count == 0)
        {
            bolts.Add(["(없음)", "0", "0", ""]);
        }
        else
        {
            bolts.Add(["합계", $"{bom.Bolts.Sum(b => b.Qty)}", "", ""]);
        }

        (string, double)[] boltCols = [("볼트", 60), ("수량", 28), ("조립수", 28), ("비고", 40)];
        foreach (var d in Tables("볼트집계표 (BOLT SCHEDULE)", boltCols, bolts, maxRows, th))
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

    public IReadOnlyList<Sheet> Compose(string prefix, string title, IEnumerable<DrawPlan> views, int firstNumber = 1)
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
                    sheets.Add(Flush(prefix, title, scale, placed, sheets.Count + firstNumber));
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
                sheets.Add(Flush(prefix, title, scale, placed, sheets.Count + firstNumber));
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
        d.Insert(Layers.Frame, frame.BlockName, ox + (frame.BaseX * scale), oy + (frame.BaseY * scale), scale / frame.BlockScale, attrs);
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
    public enum Kinds { Assembly = 1, Part = 2, Plate = 4, Plan = 8, Bom = 16, All = 31, Cover = 32, Notes = 64, Tables = 128, Anchor = 256, Docs = Cover | Notes | Tables | Anchor, Full = All | Docs }

    public static DrawingSet Generate(Project project, SectionCatalog catalog, SpliceStandards splices, SheetFrame frame, Kinds kinds = Kinds.All)
    {
        var model = new ModelBuilder(catalog, splices).Build(project);
        var set = new DrawingSet { Model = model };
        set.Warnings.AddRange(model.Warnings);
        var composer = new SheetComposer(frame, project.Name, project.Date);
        if (kinds.HasFlag(Kinds.Plan))
        {
            set.Sheets.AddRange(composer.Compose("E", "ERECTION PLAN", LayoutPlan.Generate(model, frame)));
            set.Sheets.AddRange(composer.Compose("V", "ERECTION ELEVATION", LayoutElevation.Generate(model, frame)));
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
            set.Sheets.AddRange(composer.Compose("M", "BOM / 물량표", BomSheets.Generate(model, frame)));
        }

        if (kinds.HasFlag(Kinds.Anchor))
        {
            set.Sheets.AddRange(composer.Compose("F", "ANCHOR PLAN", AnchorPlan.Generate(model, frame)));
        }

        // General sheets (G): notes and standard tables from the original HS-STEEL blocks, then the cover with the
        // index of everything (its own sheet numbers are known up front: cover, notes, then one per table scale).
        var general = new List<DrawPlan>();
        if (kinds.HasFlag(Kinds.Notes))
        {
            general.Add(HsBlocks.View(HsBlocks.Notes, "GENERAL NOTES / 일반사항", frame));
        }

        if (kinds.HasFlag(Kinds.Tables))
        {
            general.AddRange(new[] { HsBlocks.FilletWeld, HsBlocks.BracketWeld, HsBlocks.BoltData, HsBlocks.HoleDia }.Select(b => HsBlocks.View(b, b, frame)));
        }

        var first = kinds.HasFlag(Kinds.Cover) ? 2 : 1;
        if (kinds.HasFlag(Kinds.Cover))
        {
            var probe = new SheetComposer(frame, project.Name, project.Date).Compose("G", "GENERAL", general, first);
            var index = new List<(string, string, string)> { ("G-001", "COVER / 표지", "1/1") };
            index.AddRange(probe.Select(s => (s.Number, ViewTitles(s), $"1/{Annotate.F(s.Scale)}")));
            index.AddRange(set.Sheets.Select(s => (s.Number, $"{s.Title} {ViewTitles(s)}".Trim(), $"1/{Annotate.F(s.Scale)}")));
            set.Sheets.InsertRange(0, composer.Compose("G", "COVER", [CoverSheet.Generate(project.Name, project.Date, index, frame)]));
        }

        set.Sheets.InsertRange(first - 1, composer.Compose("G", "GENERAL", general, first));
        set.Warnings.AddRange(composer.Warnings);
        return set;
    }

    private static string ViewTitles(Sheet s) =>
        s.Plan.Meta["views"] is not System.Text.Json.Nodes.JsonArray a ? ""
        : a.Count <= 3 ? string.Join(", ", a.Select(x => x!.GetValue<string>())) : $"{a.Count} VIEWS";
}
