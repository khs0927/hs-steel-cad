using System.Text.RegularExpressions;
using HsSteel.Assets;
using HsSteel.Domain;
using HsSteel.Drafting;
using HsSteel.Mcp;
using HsSteel.Modeling;

namespace HsSteel.Tests;

public class DraftingLayoutTests
{
    private static DrawingSet Set()
    {
        var ws = Fx.Ws.Value;
        return DrawingSet.Generate(Demo.Sample(), ws.Catalog, ws.Splices, SheetFrame.A3Default);
    }

    [Fact]
    public void No_annotation_boxes_overlap_on_demo_sheets()
    {
        if (!Fx.HasLegacy)
        {
            return;
        }

        var hits = Set().Sheets.SelectMany(s => AnnotationBoxes.Collisions(s.Plan).Select(h => $"{s.Number}: {h}")).ToList();
        Assert.True(hits.Count == 0, $"{hits.Count} collisions:\n" + string.Join("\n", hits.Take(40)));
    }

    [Fact]
    public void Every_dimension_shows_a_real_value()
    {
        if (!Fx.HasLegacy)
        {
            return;
        }

        var num = new Regex(@"^\d+(\.\d+)?$");
        var multi = new Regex(@"^(\d+)@(\d+(?:\.\d+)?)=(\d+(?:\.\d+)?)$");
        var dims = Set().Sheets.SelectMany(s => s.Plan.Entities).Where(e => e.Type == "dimension").ToList();
        Assert.NotEmpty(dims);
        foreach (var e in dims)
        {
            var t = e.Spec["text"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(t));
            var m = multi.Match(t!);
            if (m.Success)
            {
                Assert.Equal(double.Parse(m.Groups[3].Value), int.Parse(m.Groups[1].Value) * double.Parse(m.Groups[2].Value), 1);
            }
            else
            {
                Assert.Matches(num, t!);
                Assert.True(double.Parse(t!) > 0, t);
            }
        }

        // Ordinate labels are cumulative true values.
        Assert.All(Set().Sheets.SelectMany(s => s.Plan.Entities).Where(e => e.Type == "text" && e.Layer == Layers.Dim), e => Assert.Matches(num, e.Spec["text"]!.GetValue<string>()));
    }

    [Fact]
    public void Narrow_chain_switches_to_ordinate_without_overlaps()
    {
        var d = new DrawPlan { Scale = 15 };
        Station[] st = [new(0, 0), new(40, 40), new(85, 85), new(130, 130), new(5000, 5000)];
        var depth = Annotate.Chain(d, st, true, 0, -100);
        Assert.DoesNotContain(d.Entities, e => e.Type == "dimension");
        var labels = d.Entities.Where(e => e.Type == "text").Select(e => e.Spec["text"]!.GetValue<string>()).ToList();
        Assert.Equal(["0", "40", "85", "130", "5000"], labels);
        Assert.Empty(AnnotationBoxes.Collisions(d));
        Assert.Equal(Annotate.ChainDepth(st, true, -1, 15), depth, 6);

        // Wide spacing stays a chain.
        var w = new DrawPlan { Scale = 1 };
        Annotate.Chain(w, st, true, 0, -10);
        Assert.Contains(w.Entities, e => e.Type == "dimension");
    }

    [Fact]
    public void Spread_keeps_order_gap_and_centres_clusters()
    {
        var r = Annotate.Spread([0, 1, 2, 100], 10);
        Assert.Equal([-9.0, 1, 11, 100], r);
        Assert.Equal([5.0, 50], Annotate.Spread([5, 50], 10));
    }

    [Fact]
    public void Bolt_notes_follow_hole_rules()
    {
        Assert.Equal("6-M20 HTB (Ø22)", Callouts.BoltNote(6, 22));
        Assert.Equal("4-M22 HTB (Ø24)", Callouts.BoltNote(4, 24));
        Assert.Equal("8-M24 HTB (Ø27)", Callouts.BoltNote(8, 27));
    }

    [Fact]
    public void Assembly_sheets_carry_bolt_notes_weld_symbols_marks_and_enlarged_section()
    {
        if (!Fx.HasLegacy)
        {
            return;
        }

        var r = Fx.Sample();
        foreach (var a in r.Assemblies)
        {
            var d = AssemblyDetail.Generate(a, SheetFrame.A3Default.AreaW - 10);
            var texts = d.Entities.Where(e => e.Type == "text").Select(e => e.Spec["text"]!.GetValue<string>()).ToList();
            if (a.Main.Holes.Count > 0)
            {
                Assert.Contains(texts, t => Regex.IsMatch(t, @"^\d+-M\d+ HTB \(Ø\d+\)"));
            }

            var welded = a.Attachments.Where(x => x.Welded).Select(x => x.Part.Mark).Distinct().Count();
            var welds = d.Entities.Where(e => e.Tag?["kind"]?.GetValue<string>() == "weld" && e.Type == "leader").ToList();
            Assert.Equal(welded, welds.Count);
            Assert.All(welds, e => Assert.StartsWith("HS_WELD_", e.Tag!["block"]!.GetValue<string>()));
            Assert.Contains(texts, t => t == a.Main.Mark);
            Assert.Contains(texts, t => t.StartsWith("SECTION  S=1/"));
            Assert.True(d.Meta["section_scale"]!.GetValue<double>() <= d.Scale);
            Assert.Empty(AnnotationBoxes.Collisions(d));
        }
    }

    [Fact]
    public void Part_and_plate_details_cover_every_steel_family()
    {
        if (!Fx.HasLegacy)
        {
            return;
        }

        foreach (var family in DomainProfileTests.SteelFamilies)
        {
            var rows = SectionTable.Load(Path.Combine(Fx.Attr, family + ".dat"))
                .Where(r => r.UnitWeight > 1 && !DomainProfileTests.LegacyAnomalies.ContainsKey(r.Spec))
                .OrderBy(r => r.UnitWeight)
                .ToList();
            Assert.NotEmpty(rows);
            var row = rows[Math.Min(rows.Count / 3, rows.Count - 1)];
            var profile = Profile.FromRecord(row);
            var holes = new List<Hole>();
            if (BoltGauges.For(profile, BoltGauges.DefaultBolt(profile)) is { } gauge && gauge.Lines.Count > 0)
            {
                var dia = Bolts.HoleFor(Math.Min(gauge.MaxBolt, BoltGauges.DefaultBolt(profile)));
                holes.Add(new Hole(HoleFace.Web, 40, gauge.Lines[0], dia));
                holes.Add(new Hole(HoleFace.Web, 40, gauge.Lines[^1], dia));
            }

            var part = new ShapePart { Profile = profile, Length = 6000, Holes = holes, Mark = "T1", Quantity = 1 };
            var d = PartDetail.Generate(part);
            Assert.True(d.Entities.Count >= 4, family + " " + row.Spec);
            Assert.Contains(d.Entities, e => e.Type is "polyline" or "lwpolyline" or "line" or "circle" or "arc");
            Assert.Empty(AnnotationBoxes.Collisions(d));
            Assert.Equal("part", d.Meta["kind"]!.GetValue<string>());
        }

        var plate = new PlatePart
        {
            Thickness = 12,
            Outline = [new V2(0, 0), new V2(300, 0), new V2(300, 200), new V2(0, 200)],
            Holes = [new PlateHole(50, 50, 22), new PlateHole(250, 50, 22), new PlateHole(50, 150, 22), new PlateHole(250, 150, 22)],
            Mark = "P1",
            Quantity = 2,
            Role = "SHEAR-TAB",
        };
        var pd = PlateDetail.Generate(plate);
        Assert.True(pd.Entities.Count >= 4);
        Assert.Empty(AnnotationBoxes.Collisions(pd));
        Assert.Equal("plate", pd.Meta["kind"]!.GetValue<string>());
    }

    [Fact]
    public void Fabrication_gauges_and_welds_cover_common_shapes()
    {
        var h = new Profile("H400x200x8x13", "H-BEAM", ShapeKind.I, [400, 200, 8, 13, 16, 0], 66, 1.5);
        var ang = new Profile("L100x100x10", "ANGLE", ShapeKind.L, [100, 100, 10, 10, 12, 6], 15, 0.4);
        var ch = new Profile("[150x75x6.5x10", "CHANNEL", ShapeKind.Channel, [150, 75, 6.5, 10, 10, 5], 18, 0.5);
        var box = new Profile("□100x100x4.5", "SQ-PIPE", ShapeKind.Box, [100, 100, 4.5, 4.5, 4.5, 9], 13.1, 0.4);
        var pipe = new Profile("φ165.2x7.1", "STEEL-PIPE", ShapeKind.Pipe, [165.2, 7.1], 27.3, 0.5);
        Assert.NotNull(BoltGauges.For(h, 20));
        Assert.NotNull(BoltGauges.For(ang, 16));
        Assert.NotNull(BoltGauges.For(ch, 16));
        Assert.True(WeldRules.FilletLeg(13, 12) is >= 3 and <= 12);
        Assert.Equal(22, Bolts.HoleFor(20));
        Assert.Equal(27, Bolts.HoleFor(24));
        Assert.Equal(Bolts.S10T, Bolts.GradeOf("TS M20*60"));
        Assert.True(h.Section().Count >= 1 && box.Section().Count >= 1 && pipe.Section().Count >= 1);
        Assert.NotEmpty(ang.Section());
        Assert.NotEmpty(ch.Section());
    }
}