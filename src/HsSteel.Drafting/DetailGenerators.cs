using System.Text.Json.Nodes;
using HsSteel.Domain;
using HsSteel.Modeling;

namespace HsSteel.Drafting;

/// <summary>Paper-size constants shared by the detail generators (mm on paper).</summary>
public static class Paper
{
    public const double SectionMax = 24;
    public const double LengthBudget = 230;
    public const double Text = 2.5;
    public const double DimRow = 7;
}

/// <summary>Single-part shop detail (형강 단품도) for any profile.</summary>
public static class PartDetail
{
    public static DrawPlan Generate(ShapePart part)
    {
        var p = part.Profile;
        var s = SheetFrame.ScaleFor(Math.Max(p.Depth, p.Width), Paper.SectionMax);
        double P(double v) => v * s;
        var th = P(Paper.Text);
        var d = new DrawPlan { Title = $"{part.Mark} {p.Spec}", Scale = s, CurrentTag = Tag("part", part.Mark) };
        var map = LengthMap.For(part.Length, P(Paper.LengthBudget), part.Holes.Select(h => h.X), Math.Max(100, P(10)), P(12));

        var webHoles = part.Holes.Where(h => h.Face == HoleFace.Web).ToList();
        var flgHoles = part.Holes.Where(h => h.Face != HoleFace.Web).ToList();
        var flgStations = Stations(map, part.Length, flgHoles.Select(h => h.X));
        var flgDepth = flgHoles.Count > 0 ? Annotate.ChainDepth(flgStations, true, -1, s) : 0;
        var topY = flgHoles.Count > 0 ? Math.Max(P(21), P(11) + flgDepth + P(Paper.DimRow)) : P(14);
        var frontY = topY + p.Width + P(18);
        MemberViews.Top(d, p, map, 0, topY, part.Holes, copes: part.Copes);
        MemberViews.Front(d, p, map, 0, frontY, part.Holes, copes: part.Copes);

        var above = frontY + p.Depth;
        var line = above + P(Paper.DimRow);
        if (webHoles.Count > 0)
        {
            var used = Annotate.Chain(d, Stations(map, part.Length, webHoles.Select(h => h.X)), true, above, line);
            line += Annotate.NextRow(used, s);
        }

        Annotate.Dim(d, 0, map.DrawnLength, true, above, line, Annotate.F(part.Length));
        var right = map.DrawnLength + P(Paper.DimRow);
        if (webHoles.Count > 0)
        {
            Annotate.Chain(d, webHoles.Select(h => h.Across).Distinct().Prepend(0).Append(p.Depth).Select(v => new Station(frontY + v, v)).ToList(), false, map.DrawnLength, right);
        }

        if (flgHoles.Count > 0)
        {
            Annotate.Chain(d, flgStations, true, topY, topY - P(Paper.DimRow));
            Annotate.Chain(d, flgHoles.Select(h => h.Across).Distinct().Prepend(0).Append(p.Width).Select(v => new Station(topY + v, v)).ToList(), false, map.DrawnLength, right);
        }

        // Callouts live in the band between the plan (top) view and the elevation (front) view.
        var placer = new Placer(d, P(0.8));
        placer.Block(new Box(-P(1), frontY - (p.Depth * 0.12), map.DrawnLength + P(1), frontY + (p.Depth * 1.12)));
        placer.Block(new Box(-P(1), topY - (p.Width * 0.12), map.DrawnLength + P(1), topY + (p.Width * 1.12)));
        double bandLo = topY + (p.Width * 1.12) + P(1), bandHi = frontY - (p.Depth * 0.12) - P(1);
        IEnumerable<Box> InBand(double x, double w, double h) => Placer.Band(x, w, h, bandLo, bandHi, P(2), 0, map.DrawnLength);
        var markX = map.Map(Math.Min(part.Length * 0.15, 400));
        Callouts.Balloon(d, placer, part.Mark, th, (markX, frontY), (w, h) => InBand(markX, w, h));
        BoltNotes(d, placer, map, webHoles, flgHoles, frontY, topY, th, InBand);
        CopeNotes(d, placer, map, part.Copes, frontY, p.Depth, th, InBand);

        Callouts.Section(d, p, 0, -P(2));
        d.Text(Layers.Text, $"{part.Mark}", 0, P(6), th * 1.6, "middle_left");
        d.Text(Layers.Text, $"{p.Spec}  L={Annotate.F(part.Length)}  {part.Material}  {part.Quantity} EA  {part.Weight:0.0} kg/EA", DrawPlan.TextWidth(part.Mark, th * 1.6) + P(4), P(6), th, "middle_left");
        d.Meta["kind"] = "part";
        d.Meta["mark"] = part.Mark;
        d.Meta["broken"] = map.Broken;
        return d;
    }

    /// <summary>"n-Mxx HTB (Øhole)" notes for every hole cluster, leaders from the cluster's hole nearest to the band.</summary>
    internal static void BoltNotes(DrawPlan d, Placer placer, LengthMap map, List<Hole> webHoles, List<Hole> flgHoles, double frontY, double topY, double th, Func<double, double, double, IEnumerable<Box>> band, double ox = 0)
    {
        foreach (var c in Callouts.Clusters(webHoles))
        {
            foreach (var g in c.GroupBy(h => h.Dia).OrderBy(g => g.Key))
            {
                var low = g.OrderBy(h => h.Across).ThenBy(h => h.X).First();
                var anchor = (ox + map.Map(low.X), frontY + low.Across - (low.Dia / 2));
                Callouts.Note(d, placer, Callouts.BoltNote(g.Count(), g.Key), th * 0.9, anchor, (w, h) => band(anchor.Item1, w, h));
            }
        }

        foreach (var c in Callouts.Clusters(flgHoles))
        {
            foreach (var g in c.GroupBy(h => h.Dia).OrderBy(g => g.Key))
            {
                var perFlange = g.GroupBy(h => h.Face).Max(f => f.Count());
                var faces = g.Select(h => h.Face).Distinct().Count();
                var hi = g.OrderByDescending(h => h.Across).ThenBy(h => h.X).First();
                var anchor = (ox + map.Map(hi.X), topY + hi.Across + (hi.Dia / 2));
                var text = Callouts.BoltNote(perFlange, g.Key) + (faces > 1 ? " EA FLG" : "");
                Callouts.Note(d, placer, text, th * 0.9, anchor, (w, h) => band(anchor.Item1, w, h));
            }
        }
    }


    /// <summary>One cope note per end (top+bottom share the station), leaders into the view band.</summary>
    internal static void CopeNotes(DrawPlan d, Placer placer, LengthMap map, IReadOnlyList<FlangeCope> copes, double frontY, double depth, double th, Func<double, double, double, IEnumerable<Box>> band, double ox = 0)
    {
        if (copes is null || copes.Count == 0)
        {
            return;
        }

        foreach (var g in copes.GroupBy(c => c.Corner is CopeCorner.TopStart or CopeCorner.BottomStart))
        {
            var c = g.OrderByDescending(x => x.Length).First();
            var start = g.Key;
            var xTrue = start ? Math.Min(c.Length, map.Length) : Math.Max(0, map.Length - c.Length);
            var anchor = (ox + map.Map(xTrue), frontY + depth - (c.Depth / 2));
            Callouts.Note(d, placer, Callouts.CopeNote(c), th * 0.85, anchor, (w, h) => band(anchor.Item1, w, h));
        }

        d.Meta["cope_notes"] = (d.Meta["cope_notes"]?.GetValue<int>() ?? 0) + 1;
    }

    internal static List<Station> Stations(LengthMap map, double length, IEnumerable<double> xs) =>
        xs.Distinct().Prepend(0).Append(length).Select(x => new Station(map.Map(x), x)).ToList();

    internal static JsonObject Tag(string kind, string mark) => new() { ["kind"] = kind, ["mark"] = mark };
}

/// <summary>Plate detail (PLATE 단품도): outline, holes, chained dimensions, label.</summary>
public static class PlateDetail
{
    public static DrawPlan Generate(PlatePart part)
    {
        var s = SheetFrame.ScaleFor(Math.Max(part.SizeU, part.SizeV), 70);
        double P(double v) => v * s;
        var th = P(Paper.Text);
        var d = new DrawPlan { Title = $"{part.Mark} {part.Name}", Scale = s, CurrentTag = PartDetail.Tag("plate", part.Mark) };
        var holesU = part.Holes.Select(h => h.U).Distinct().ToList();
        var holesV = part.Holes.Select(h => h.V).Distinct().ToList();
        double ox = P(Paper.DimRow * (holesV.Count > 0 ? 2 : 1)) + P(3), oy = P(Paper.DimRow * (holesU.Count > 0 ? 2 : 1)) + P(10);

        d.Polyline(Layers.Outline, true, [.. part.Outline.Select(v => (ox + v.X, oy + v.Y))]);
        foreach (var h in part.Holes)
        {
            d.Circle(Layers.Hole, ox + h.U, oy + h.V, h.Dia / 2);
            var c = h.Dia * 0.7;
            d.Line(Layers.Center, ox + h.U - c, oy + h.V, ox + h.U + c, oy + h.V);
            d.Line(Layers.Center, ox + h.U, oy + h.V - c, ox + h.U, oy + h.V + c);
        }

        var line = oy - P(Paper.DimRow);
        if (holesU.Count > 0)
        {
            line -= Annotate.NextRow(Annotate.Chain(d, holesU.Prepend(0).Append(part.SizeU).Select(u => new Station(ox + u, u)).ToList(), true, oy, line), s);
        }

        Annotate.Dim(d, ox, ox + part.SizeU, true, oy, line, Annotate.F(part.SizeU));
        line = ox - P(Paper.DimRow);
        if (holesV.Count > 0)
        {
            line -= Annotate.NextRow(Annotate.Chain(d, holesV.Prepend(0).Append(part.SizeV).Select(v => new Station(oy + v, v)).ToList(), false, ox, line), s);
        }

        Annotate.Dim(d, oy, oy + part.SizeV, false, ox, line, Annotate.F(part.SizeV));
        var holeNote = part.Holes.GroupBy(h => h.Dia).OrderBy(g => g.Key).Select(g => Callouts.BoltNote(g.Count(), g.Key));
        var label = $"{part.Name}  {part.Material}  {part.Quantity} EA  {part.Weight:0.0} kg/EA";
        var ly = oy + part.SizeV + P(6);
        d.Text(Layers.Text, part.Mark, ox, ly + P(5), th * 1.6, "middle_left");
        d.Text(Layers.Text, label, ox, ly, th, "middle_left");
        if (part.Holes.Count > 0)
        {
            d.Text(Layers.Text, string.Join(", ", holeNote), ox, ly - P(4), th * 0.85, "middle_left");
        }

        d.Meta["kind"] = "plate";
        d.Meta["mark"] = part.Mark;
        return d;
    }
}

/// <summary>Assembly shop drawing (Assy 제작도): main member with attached plates, marks, dimensions, part list.</summary>
public static class AssemblyDetail
{
    /// <summary>Generates the drawing, shortening the drawn length (bigger break) until it fits <paramref name="maxPaperWidth"/>.</summary>
    public static DrawPlan Generate(Assembly a, double maxPaperWidth = 300)
    {
        DrawPlan d = null!;
        foreach (var f in new[] { 0.82, 0.72, 0.62, 0.52, 0.42 })
        {
            d = GenerateWithBudget(a, f);
            var (x0, _, x1, _) = d.Extents();
            if ((x1 - x0) / d.Scale <= maxPaperWidth)
            {
                break;
            }
        }

        return d;
    }

    private static DrawPlan GenerateWithBudget(Assembly a, double budgetFactor)
    {
        var main = a.Main;
        var p = main.Profile;
        var s = SheetFrame.ScaleFor(Math.Max(p.Depth, p.Width) * 1.4, Paper.SectionMax * 1.2);
        double P(double v) => v * s;
        var th = P(Paper.Text);
        var d = new DrawPlan { Title = $"{a.Mark} {a.Type.ToString().ToUpperInvariant()}", Scale = s, CurrentTag = PartDetail.Tag("assembly", a.Mark) };

        var features = main.Holes.Select(h => h.X).ToList();
        foreach (var at in a.Attachments)
        {
            var (x0, x1) = XRange(at);
            features.Add(Math.Clamp(x0, 0, main.Length));
            features.Add(Math.Clamp(x1, 0, main.Length));
        }

        var map = LengthMap.For(main.Length, P(Paper.LengthBudget * budgetFactor), features, Math.Max(100, P(10)), P(12));
        var minX = a.Attachments.Select(at => XRange(at).A).Append(0).Min();
        var ox = Math.Max(0, -map.Map(minX)) + P(Paper.DimRow);
        var markRow = P(12);
        var flg = main.Holes.Where(h => h.Face != HoleFace.Web).Select(h => h.X).ToList();
        var flgStations = PartDetail.Stations(map, main.Length, flg).Select(v => v with { Drawn = v.Drawn + ox }).ToList();
        var flgDepth = flg.Count > 0 ? Annotate.ChainDepth(flgStations, true, -1, s) : 0;
        var below = MaxBelow(a, p);
        var topY = Math.Max(markRow + P(Paper.DimRow * 2), markRow + P(6) + flgDepth + P(Paper.DimRow)) + below;
        var topAbove = MaxAbove(a, p, top: true);
        var frontY = topY + p.Width + topAbove + below + P(24);

        MemberViews.Top(d, p, map, ox, topY, main.Holes, copes: main.Copes);
        MemberViews.Front(d, p, map, ox, frontY, main.Holes, copes: main.Copes);
        foreach (var at in a.Attachments)
        {
            DrawAttachment(d, at, map, ox, frontY, topY);
        }

        // Dimensions: chain of holes + attachment stations above the front view, overall above that.
        var above = frontY + p.Depth + MaxAbove(a, p, top: false);
        var stations = main.Holes.Where(h => h.Face == HoleFace.Web).Select(h => h.X)
            .Concat(a.Attachments.Where(at => at.Welded).SelectMany(at => new[] { XRange(at).A, XRange(at).B }))
            .Where(x => x >= 0 && x <= main.Length).ToList();
        var line = above + P(Paper.DimRow);
        if (stations.Count > 0)
        {
            var used = Annotate.Chain(d, PartDetail.Stations(map, main.Length, stations).Select(v => v with { Drawn = v.Drawn + ox }).ToList(), true, above, line);
            line += Annotate.NextRow(used, s);
        }

        Annotate.Dim(d, ox, ox + map.DrawnLength, true, above, line, Annotate.F(main.Length));
        if (flg.Count > 0)
        {
            Annotate.Chain(d, flgStations, true, topY - below, topY - below - P(Paper.DimRow));
        }

        // Callouts: bolt notes, weld symbols and the main mark in the band between the views; plate marks in one row below.
        var placer = new Placer(d, P(0.8));
        var right = ox + map.DrawnLength;
        var frontBox = new Box(0, frontY - below - (p.Depth * 0.12), right + P(1), above + (p.Depth * 0.12));
        var topBox = new Box(0, topY - below - (p.Width * 0.12), right + P(1), topY + p.Width + topAbove + (p.Width * 0.12));
        placer.Block(frontBox);
        placer.Block(topBox);
        double bandLo = topBox.Y1 + P(1), bandHi = frontBox.Y0 - P(1);
        var xMax = Math.Max(right, P(150));
        IEnumerable<Box> InBand(double x, double w, double h) => Placer.Band(x, w, h, bandLo, bandHi, P(2), 0, xMax);

        var mainX = ox + map.Map(Math.Min(main.Length * 0.15, 400));
        Callouts.Balloon(d, placer, main.Mark, th, (mainX, frontY - below), (w, h) => InBand(mainX, w, h));
        foreach (var g in a.Attachments.Where(at => at.Welded).GroupBy(at => at.Part.Mark).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var at = g.First();
            var kind = at.At.Orientation == PlateOrientation.End && at.Part.Thickness >= 12 ? WeldKind.Groove : WeldKind.Fillet;
            var size = kind == WeldKind.Fillet ? Callouts.FilletSize(at.Part.Thickness) : at.Part.Thickness;
            var anchor = Callouts.FrontAnchor(at, x => ox + map.Map(x), frontY);
            Callouts.Weld(d, placer, kind, size, anchor, (w, h) => InBand(anchor.X, w, h), g.Key);
        }

        PartDetail.BoltNotes(d, placer, map, main.Holes.Where(h => h.Face == HoleFace.Web).ToList(), main.Holes.Where(h => h.Face != HoleFace.Web).ToList(), frontY, topY, th, InBand, ox);
        PartDetail.CopeNotes(d, placer, map, main.Copes, frontY, p.Depth, th, InBand, ox);

        foreach (var g in a.Attachments.GroupBy(at => at.Part.Mark).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var at = g.First();
            var (x0, x1) = XRange(at);
            var cx = ox + map.Map((x0 + x1) / 2);
            var cy = TopCenterY(at, topY);
            Callouts.Balloon(d, placer, g.Key, th, (cx, cy), (w, h) => Placer.Band(cx, w, h, markRow - (h / 2), markRow + (h / 2), P(1), 0, xMax).Where(b => b.X0 >= 0));
        }

        // Part list.
        var rows = new List<string[]> { new[] { main.Mark, p.Spec, Annotate.F(main.Length), "1", $"{main.Weight:0.0}", $"{main.Weight:0.0}" } };
        foreach (var g in a.Attachments.GroupBy(at => at.Part.Mark).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var part = g.First().Part;
            rows.Add([g.Key, part.Name, "", $"{g.Count()}", $"{part.Weight:0.0}", $"{part.Weight * g.Count():0.0}", g.First().Welded ? "" : "LOOSE"]);
        }

        foreach (var b in a.Bolts)
        {
            rows.Add(["", b.Name, "", $"{b.Count}", "", "", "BOLT"]);
        }

        rows.Add(["", "TOTAL", "", "", "", $"{a.Weight:0.0}", ""]);
        (string, double)[] cols = [("MARK", P(14)), ("DESCRIPTION", P(38)), ("LENGTH", P(16)), ("Q'TY", P(10)), ("UNIT(kg)", P(16)), ("TOTAL(kg)", P(18)), ("REMARK", P(15))];
        var head = $"{a.Mark}  ({a.Quantity} EA)";
        d.Text(Layers.Text, head, ox, markRow - P(10), th * 2, "middle_left");
        d.Text(Layers.Text, $"{a.Type.ToString().ToUpperInvariant()}   W = {a.Weight:0.0} kg/EA   ({string.Join(",", a.Members.Take(12))}{(a.Members.Count > 12 ? ",..." : "")})", ox + DrawPlan.TextWidth(head, th * 2) + P(4), markRow - P(10), th * 0.9, "middle_left");
        var tableTop = markRow - P(20);
        Annotate.Table(d, ox, tableTop, cols, rows, th * 0.9, $"{a.Mark}  PART LIST");

        // Enlarged section in the bottom band, right of the part list.
        Callouts.Section(d, p, ox + cols.Sum(c => c.Item2) + P(8), tableTop + P(4));
        d.Meta["kind"] = "assembly";
        d.Meta["mark"] = a.Mark;
        d.Meta["broken"] = map.Broken;
        return d;
    }

    /// <summary>X extent of an attachment along the main member.</summary>
    public static (double A, double B) XRange(Attachment at)
    {
        var o = at.At.Origin.X;
        return at.At.Orientation switch
        {
            PlateOrientation.End => at.At.NSign > 0 ? (o, o + at.Part.Thickness) : (o - at.Part.Thickness, o),
            _ => (o, o + at.Part.SizeU),
        };
    }

    private static double TopCenterY(Attachment at, double topY)
    {
        var o = at.At.Origin;
        var t = at.Part.Thickness;
        return at.At.Orientation switch
        {
            PlateOrientation.Flange => topY + o.Z + (at.Part.SizeV / 2),
            PlateOrientation.Web => topY + o.Z + (at.At.NSign * t / 2),
            _ => topY + o.Z + (at.At.USign * at.Part.SizeU / 2),
        };
    }

    /// <summary>How far attachments stick out below the section (front: below y=0; top: below z=0).</summary>
    private static double MaxBelow(Assembly a, Profile p) =>
        a.Attachments.Select(at => at.At.Orientation switch
        {
            PlateOrientation.End => Math.Max(-at.At.Origin.Y, -Math.Min(at.At.Origin.Z, at.At.Origin.Z + (at.At.USign * at.Part.SizeU))),
            PlateOrientation.Flange => Math.Max(at.At.NSign < 0 ? at.Part.Thickness - at.At.Origin.Y : 0, -at.At.Origin.Z),
            _ => -Math.Min(at.At.Origin.Z, at.At.Origin.Z + (at.At.NSign * at.Part.Thickness)),
        }).Append(0).Max();

    private static double MaxAbove(Assembly a, Profile p, bool top) =>
        a.Attachments.Select(at =>
        {
            var o = at.At.Origin;
            if (top)
            {
                var z1 = at.At.Orientation switch
                {
                    PlateOrientation.Flange => o.Z + at.Part.SizeV,
                    PlateOrientation.Web => o.Z + (at.At.NSign * at.Part.Thickness),
                    _ => Math.Max(o.Z, o.Z + (at.At.USign * at.Part.SizeU)),
                };
                return z1 - p.Width;
            }

            var y1 = at.At.Orientation switch
            {
                PlateOrientation.Flange => o.Y + Math.Max(0, at.At.NSign * at.Part.Thickness),
                _ => o.Y + at.Part.SizeV,
            };
            return y1 - p.Depth;
        }).Append(0).Max();

    private static void DrawAttachment(DrawPlan d, Attachment at, LengthMap map, double ox, double frontY, double topY)
    {
        var layer = at.Welded ? Layers.Plate : Layers.Loose;
        var pl = at.Part;
        var o = at.At.Origin;
        var t = pl.Thickness;
        double X(double x) => ox + map.Map(x);
        void Box(double x0, double x1, double y0, double y1) =>
            d.Polyline(layer, true, (X(Math.Min(x0, x1)), Math.Min(y0, y1)), (X(Math.Max(x0, x1)), Math.Min(y0, y1)), (X(Math.Max(x0, x1)), Math.Max(y0, y1)), (X(Math.Min(x0, x1)), Math.Max(y0, y1)));

        switch (at.At.Orientation)
        {
            case PlateOrientation.Web:
                d.Polyline(layer, true, [.. pl.Outline.Select(v => (X(o.X + v.X), frontY + o.Y + v.Y))]);
                foreach (var h in pl.Holes)
                {
                    d.Circle(Layers.Hole, X(o.X + h.U), frontY + o.Y + h.V, h.Dia / 2);
                }

                Box(o.X, o.X + pl.SizeU, topY + o.Z, topY + o.Z + (at.At.NSign * t));
                break;
            case PlateOrientation.Flange:
                Box(o.X, o.X + pl.SizeU, frontY + o.Y, frontY + o.Y + (at.At.NSign * t));
                d.Polyline(layer, true, [.. pl.Outline.Select(v => (X(o.X + v.X), topY + o.Z + v.Y))]);
                foreach (var h in pl.Holes)
                {
                    d.Circle(Layers.Hole, X(o.X + h.U), topY + o.Z + h.V, h.Dia / 2);
                }

                break;
            default:
                var x1 = o.X + (at.At.NSign * t);
                Box(o.X, x1, frontY + o.Y, frontY + o.Y + pl.SizeV);
                Box(o.X, x1, topY + o.Z, topY + o.Z + (at.At.USign * pl.SizeU));
                break;
        }
    }
}
