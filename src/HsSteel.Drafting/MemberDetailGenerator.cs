using HsSteel.Domain;

namespace HsSteel.Drafting;

/// <summary>
/// Single-part shop detail (단품 상세도) for a straight H member:
/// elevation (web face) + plan (top flange) + end section, end-hole layout, scallops,
/// chained dimensions in true values, mark/spec/qty label, placed in the company sheet frame.
/// Long members are drawn with a break so the section stays legible.
/// </summary>
public sealed class MemberDetailGenerator(DetailRules rules, SheetFrame frame)
{
    /// <summary>Largest paper height allowed for the section depth, in mm.</summary>
    private const double SectionPaper = 28;

    public DrawPlan Generate(Member m, double originX = 0, double originY = 0)
    {
        var s = m.Section;
        var plan = new DrawPlan { Title = $"{m.Mark} {s.Spec} L={m.Length:0}" };

        // Scale from the section (legibility), not from the length.
        var scale = SheetFrame.StandardScales.FirstOrDefault(k => s.H / k <= SectionPaper, SheetFrame.StandardScales[^1]);
        plan.Scale = scale;
        double P(double paperMm) => paperMm * scale;
        var th = P(rules.TextHeight);
        var gapViews = P(18);

        var budget = P(frame.AreaW) - (s.B + gapViews + P(30));
        var map = new LengthMap(m.Length, budget, EndZone(m), P(14));

        var ox = originX + P(frame.AreaX) + P(20);
        var top = originY + P(frame.AreaY + frame.AreaH - 12 - (rules.DimGap * 2));
        var elevY = top - s.H;
        var planY = elevY - gapViews - s.B;

        DrawElevation(plan, m, map, ox, elevY);
        DrawPlanView(plan, m, map, ox, planY);
        DrawSection(plan, s, ox + map.DrawnLength + gapViews + (s.B / 2), elevY + (s.H / 2), th);

        plan.Dim(Layers.Dim, (ox, elevY + s.H), (ox + map.DrawnLength, elevY + s.H), (ox, elevY + s.H + P(rules.DimGap * 2)), 0, $"{m.Length:0}");
        DimensionHoles(plan, m, map, ox, planY, P(rules.DimGap));

        plan.Text(Layers.Text, $"{m.Mark}  ({m.Quantity} EA)", ox, planY - P(22), th * 1.6, "middle_left");
        plan.Text(Layers.Text, $"{s.Spec}  L={m.Length:0}  {m.Material}  {m.Weight:0.0} kg/EA", ox, planY - P(28), th, "middle_left");
        plan.Text(Layers.Text, $"SCALE 1/{scale:0.##}", ox, planY - P(33), th * 0.8, "middle_left");
        plan.Insert(Layers.Frame, frame.BlockName, originX, originY, scale);

        plan.Meta["mark"] = m.Mark;
        plan.Meta["spec"] = s.Spec;
        plan.Meta["length"] = m.Length;
        plan.Meta["qty"] = m.Quantity;
        plan.Meta["weight_kg"] = m.Weight;
        plan.Meta["broken"] = map.Broken;
        return plan;
    }

    private double EndZone(Member m)
    {
        var zone = Math.Max(rules.Scallop * 2, 150);
        foreach (var (_, g) in m.Holes ?? [])
        {
            zone = Math.Max(zone, g.FromEnd + ((g.Rows - 1) * g.Pitch) + 100);
        }

        return zone;
    }

    private void DrawElevation(DrawPlan d, Member m, LengthMap map, double x, double y)
    {
        var s = m.Section;
        foreach (var (a, b) in map.Segments)
        {
            var xa = x + map.Map(a);
            var xb = x + map.Map(b);
            d.Line(Layers.Outline, xa, y, xb, y);
            d.Line(Layers.Outline, xa, y + s.H, xb, y + s.H);
            d.Line(Layers.Outline, xa, y + s.Tf, xb, y + s.Tf);
            d.Line(Layers.Outline, xa, y + s.H - s.Tf, xb, y + s.H - s.Tf);
            d.Line(Layers.Center, xa, y + (s.H / 2), xb, y + (s.H / 2));
        }

        d.Line(Layers.Outline, x, y, x, y + s.H);
        d.Line(Layers.Outline, x + map.DrawnLength, y, x + map.DrawnLength, y + s.H);
        if (map.Broken)
        {
            BreakSymbol(d, x + map.Map(map.BreakAt), y - (s.H * 0.15), y + (s.H * 1.15), map.GapDrawn);
        }

        if (m.Scallop)
        {
            var r = rules.Scallop;
            d.Arc(Layers.Outline, x, y + s.Tf, r, 0, 90);
            d.Arc(Layers.Outline, x, y + s.H - s.Tf, r, 270, 360);
            d.Arc(Layers.Outline, x + map.DrawnLength, y + s.Tf, r, 90, 180);
            d.Arc(Layers.Outline, x + map.DrawnLength, y + s.H - s.Tf, r, 180, 270);
        }

        DrawHoles(d, m, map, "web", x, y, s.H);
    }

    private static void DrawPlanView(DrawPlan d, Member m, LengthMap map, double x, double y)
    {
        var s = m.Section;
        foreach (var (a, b) in map.Segments)
        {
            var xa = x + map.Map(a);
            var xb = x + map.Map(b);
            d.Line(Layers.Outline, xa, y, xb, y);
            d.Line(Layers.Outline, xa, y + s.B, xb, y + s.B);
            d.Line(Layers.Hidden, xa, y + ((s.B - s.Tw) / 2), xb, y + ((s.B - s.Tw) / 2));
            d.Line(Layers.Hidden, xa, y + ((s.B + s.Tw) / 2), xb, y + ((s.B + s.Tw) / 2));
        }

        d.Line(Layers.Outline, x, y, x, y + s.B);
        d.Line(Layers.Outline, x + map.DrawnLength, y, x + map.DrawnLength, y + s.B);
        if (map.Broken)
        {
            BreakSymbol(d, x + map.Map(map.BreakAt), y - (s.B * 0.15), y + (s.B * 1.15), map.GapDrawn);
        }

        DrawHoles(d, m, map, "flange", x, y, s.B);
    }

    private static void DrawHoles(DrawPlan d, Member m, LengthMap map, string face, double x, double y, double faceWidth)
    {
        foreach (var (end, g) in m.Holes ?? [])
        {
            if (g.Face != face)
            {
                continue;
            }

            foreach (var (along, across) in HolePositions(m, end, g, faceWidth))
            {
                d.Circle(Layers.Hole, x + map.Map(along), y + across, g.HoleDia / 2);
            }
        }
    }

    private static void DrawSection(DrawPlan d, HSection s, double cx, double cy, double th)
    {
        double hw = s.B / 2, hh = s.H / 2, tw = s.Tw / 2, tf = s.Tf;
        d.Polyline(Layers.Outline, true,
            (cx - hw, cy - hh), (cx + hw, cy - hh), (cx + hw, cy - hh + tf), (cx + tw, cy - hh + tf),
            (cx + tw, cy + hh - tf), (cx + hw, cy + hh - tf), (cx + hw, cy + hh), (cx - hw, cy + hh),
            (cx - hw, cy + hh - tf), (cx - tw, cy + hh - tf), (cx - tw, cy - hh + tf), (cx - hw, cy - hh + tf));
        d.Text(Layers.Text, "SECTION", cx, cy - hh - (th * 2.5), th);
    }

    /// <summary>Chain dimension of hole rows from both ends (true values), under the plan view.</summary>
    private static void DimensionHoles(DrawPlan d, Member m, LengthMap map, double x, double planY, double gap)
    {
        var rows = new SortedSet<double>();
        foreach (var (end, g) in m.Holes ?? [])
        {
            for (var r = 0; r < g.Rows; r++)
            {
                var a = g.FromEnd + (r * g.Pitch);
                rows.Add(end == MemberEnd.Start ? a : m.Length - a);
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        var stations = new List<double> { 0 };
        stations.AddRange(rows);
        stations.Add(m.Length);
        for (var i = 0; i + 1 < stations.Count; i++)
        {
            double a = stations[i], b = stations[i + 1];
            if (b - a > 1e-6)
            {
                d.Dim(Layers.Dim, (x + map.Map(a), planY), (x + map.Map(b), planY), (x + map.Map(a), planY - gap), 0, $"{b - a:0.#}");
            }
        }
    }

    private static IEnumerable<(double Along, double Across)> HolePositions(Member m, MemberEnd end, BoltGroup g, double faceWidth)
    {
        var first = (faceWidth - ((g.Lines - 1) * g.Gauge)) / 2;
        for (var r = 0; r < g.Rows; r++)
        {
            var a = g.FromEnd + (r * g.Pitch);
            var along = end == MemberEnd.Start ? a : m.Length - a;
            for (var l = 0; l < g.Lines; l++)
            {
                yield return (along, first + (l * g.Gauge));
            }
        }
    }

    private static void BreakSymbol(DrawPlan d, double x, double y0, double y1, double gap)
    {
        var h = y1 - y0;
        var z = gap * 0.25;
        foreach (var bx in new[] { x - (gap / 2), x + (gap / 2) })
        {
            d.Polyline(Layers.Break, false, (bx, y0), (bx, y0 + (h * 0.45)), (bx + z, y0 + (h * 0.5)), (bx - z, y0 + (h * 0.55)), (bx, y0 + (h * 0.6)), (bx, y1));
        }
    }
}

/// <summary>Maps true positions along a member to drawn positions, compressing the middle when it does not fit.</summary>
public sealed class LengthMap
{
    public LengthMap(double length, double budget, double endZone, double gapDrawn)
    {
        Length = length;
        GapDrawn = gapDrawn;
        Broken = length > budget && length > (2 * endZone) + gapDrawn;
        if (Broken)
        {
            Keep = Math.Min(Math.Max(endZone, (budget - gapDrawn) / 2), (length - gapDrawn) / 2);
            Removed = length - (2 * Keep) - gapDrawn;
            BreakAt = length / 2;
        }
    }

    public double Length { get; }

    public bool Broken { get; }

    public double Keep { get; }

    public double Removed { get; }

    public double GapDrawn { get; }

    public double BreakAt { get; }

    public double DrawnLength => Broken ? Length - Removed : Length;

    /// <summary>True-length segments that are actually drawn.</summary>
    public IEnumerable<(double A, double B)> Segments =>
        Broken ? [(0, Keep), (Length - Keep, Length)] : [(0, Length)];

    public double Map(double t)
    {
        if (!Broken || t <= Keep)
        {
            return t;
        }

        return t >= Length - Keep ? t - Removed : Keep + (GapDrawn / 2);
    }
}

