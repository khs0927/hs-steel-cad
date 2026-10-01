using ACadSharp.IO;
using HsSteel.Assets;
using HsSteel.Domain;
using HsSteel.Drafting;

namespace HsSteel.Tests;

public class DraftingTests
{
    /// <summary>Original HS-STEEL install (reference assets). Tests that need it are skipped when absent.</summary>
    private static readonly string Legacy = Environment.GetEnvironmentVariable("HS_STEEL_LEGACY") ?? @"C:\HS-STEEL\HSSTEEL";

    private static string Attr(string file) => Path.Combine(Legacy, "attributes", file);

    private static Member SampleBeam(double length) => new(
        "B1", HSection.Parse("H400x200x8x13"), length, Quantity: 4,
        Holes:
        [
            (MemberEnd.Start, new BoltGroup("web", 40, 3, 70, 2, 60, 22)),
            (MemberEnd.End, new BoltGroup("web", 40, 3, 70, 2, 60, 22)),
        ],
        Scallop: true);

    [Fact]
    public void Reads_all_legacy_section_tables()
    {
        if (!Directory.Exists(Legacy))
        {
            return;
        }

        var files = Directory.GetFiles(Path.Combine(Legacy, "attributes"), "*.dat").Where(f => !f.EndsWith("Project.dat")).ToList();
        Assert.Equal(25, files.Count);
        var h = SectionTable.Load(Attr("H-BEAM.dat"));
        var row = Assert.Single(h, r => r.Spec == "H100x100x6x8");
        Assert.Equal([100, 100, 6, 8, 10], row.M.Take(5));
        Assert.Equal(17.2, row.UnitWeight);
        Assert.All(files, f => Assert.NotEmpty(SectionTable.Load(f)));
    }

    [Fact]
    public void Reads_detail_rules_from_project_dat()
    {
        if (!File.Exists(Attr("Project.dat")))
        {
            return;
        }

        var p = ProjectSettings.Load(Attr("Project.dat"));
        Assert.Equal("00공장 신축공사", p["PROJECT"]);
        var rules = DetailRules.From(p);
        Assert.Equal(30, rules.Scallop);
        Assert.Equal(40, rules.EndGauge);
        Assert.Equal(22, rules.HoleDia);
    }

    [Fact]
    public void Parsed_weight_is_close_to_table_weight()
    {
        // Independent check: theory (area x 7.85) vs HS-STEEL table (which includes fillets).
        var theory = HSection.Parse("H100x100x6x8").UnitWeight;
        Assert.InRange(theory, 17.2 * 0.95, 17.2 * 1.0);
    }

    [Fact]
    public void Length_map_keeps_ends_true_and_compresses_middle()
    {
        var map = new LengthMap(12000, 4000, 400, 200);
        Assert.True(map.Broken);
        Assert.Equal(40, map.Map(40));
        Assert.Equal(map.DrawnLength - 40, map.Map(12000 - 40), 6);
        Assert.True(map.DrawnLength <= 4000 + 1e-6);
        Assert.False(new LengthMap(2000, 4000, 400, 200).Broken);
    }

    [Fact]
    public void Detail_contains_views_holes_and_true_dimensions()
    {
        var plan = new MemberDetailGenerator(new DetailRules(), SheetFrame.A3Default).Generate(SampleBeam(9000));
        var holes = plan.Entities.Count(e => e.Type == "circle" && e.Layer == Layers.Hole);
        Assert.Equal(12, holes); // 2 ends x 3 rows x 2 lines
        var dimTexts = plan.Entities.Where(e => e.Type == "dimension").Select(e => e.Spec["text"]!.GetValue<string>()).ToList();
        Assert.Contains("9000", dimTexts);
        Assert.Contains("40", dimTexts);
        Assert.Contains("70", dimTexts);
        Assert.Contains("8640", dimTexts); // 9000 - 2*(40+140)
        Assert.True((bool)plan.Meta["broken"]!);
        Assert.Equal(15, plan.Scale); // H400 -> 26.7 mm on paper
        Assert.Single(plan.Entities, e => e.Type == "insert");
    }

    [Fact]
    public void Plan_is_deterministic()
    {
        var g = new MemberDetailGenerator(new DetailRules(), SheetFrame.A3Default);
        Assert.Equal(g.Generate(SampleBeam(6000)).ToJsonString(), g.Generate(SampleBeam(6000)).ToJsonString());
    }

    [Fact]
    public void Plan_fits_inside_frame_drawing_area()
    {
        var f = SheetFrame.A3Default;
        var plan = new MemberDetailGenerator(new DetailRules(), f).Generate(SampleBeam(15000));
        var (x0, y0, x1, y1) = plan.Extents();
        var k = plan.Scale;
        Assert.True(x0 >= f.AreaX * k && y0 >= f.AreaY * k, $"min {x0},{y0} k={k}");
        Assert.True(x1 <= (f.AreaX + f.AreaW) * k && y1 <= (f.AreaY + f.AreaH) * k, $"max {x1},{y1} k={k}");
    }

    [Fact]
    public void Writes_dxf_that_reads_back()
    {
        var plan = new MemberDetailGenerator(new DetailRules(), SheetFrame.A3Default).Generate(SampleBeam(9000));
        var path = Path.Combine(Path.GetTempPath(), $"hs_{Guid.NewGuid():N}.dxf");
        DxfExporter.Write([plan], SheetFrame.A3Default, path);
        var doc = DxfReader.Read(path);
        Assert.True(doc.Entities.Count() > plan.Entities.Count);
        Assert.Equal(12, doc.Entities.Count(e => e.GetType() == typeof(ACadSharp.Entities.Circle)));
        File.Delete(path);
    }
}

