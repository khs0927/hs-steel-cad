using System.Text.Json.Nodes;
using HsSteel.Assets;
using HsSteel.Domain;
using HsSteel.Drafting;
using HsSteel.Modeling;
using HsSteel.Mcp;

namespace HsSteel.Tests;

/// <summary>D4: assembly elevations, BOM tables/CSV, cope drawing, weld leader min length.</summary>
public class DraftingD4Tests
{
    [LegacyAssetFact]
    public void BomTable_totals_match_model_and_csv_has_korean_sections()
    {
        var r = Fx.Sample();
        var bom = BomTable.From(r);
        Assert.Equal(r.Assemblies.Count, bom.Assemblies.Count);
        Assert.Equal(
            Math.Round(r.ShapeParts.Sum(p => p.Weight * p.Quantity) + r.PlateParts.Sum(p => p.Weight * p.Quantity), 1),
            bom.TotalKg);
        Assert.Equal(r.Assemblies.Sum(a => a.Quantity), bom.AssemblyQty);

        var csv = bom.ToCsv();
        Assert.Contains("조립목록", csv, StringComparison.Ordinal);
        Assert.Contains("자재집계표", csv, StringComparison.Ordinal);
        Assert.Contains("볼트집계표", csv, StringComparison.Ordinal);
        Assert.Contains("마크,종류,주부재", csv, StringComparison.Ordinal);

        var json = JsonNode.Parse(bom.ToJson())!;
        Assert.Equal(bom.TotalKg, json["total_kg"]!.GetValue<double>());
        Assert.True(json["assemblies"]!.AsArray().Count > 0);
    }

    [LegacyAssetFact]
    public void Bom_sheets_include_assembly_material_and_bolt_tables()
    {
        var set = DrawingSet.Generate(Demo.Sample(), Fx.Ws.Value.Catalog, Fx.Ws.Value.Splices, SheetFrame.A3Default, DrawingSet.Kinds.Bom);
        Assert.Contains(set.Sheets, s => s.Number.StartsWith("M-", StringComparison.Ordinal));
        var titles = set.Sheets.SelectMany(s => s.Plan.Entities)
            .Where(e => e.Type == "text" && e.Layer == Layers.Table)
            .Select(e => e.Spec["text"]!.GetValue<string>())
            .ToList();
        Assert.Contains(titles, t => t.Contains("조립목록", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("자재집계표", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("볼트집계표", StringComparison.Ordinal));
    }

    [LegacyAssetFact]
    public void Layout_plan_kinds_also_emit_elevation_sheets()
    {
        var set = DrawingSet.Generate(Demo.Sample(), Fx.Ws.Value.Catalog, Fx.Ws.Value.Splices, SheetFrame.A3Default, DrawingSet.Kinds.Plan);
        var elev = set.Sheets.Where(s => s.Number.StartsWith("V-", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(elev);
        var texts = elev.SelectMany(s => s.Plan.Entities).Where(e => e.Type == "text").Select(e => e.Spec["text"]!.GetValue<string>()).ToList();
        Assert.Contains(texts, t => t.StartsWith("ELEVATION", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("EL. GRID", StringComparison.Ordinal));
    }

    [LegacyAssetFact]
    public void Part_detail_draws_flange_copes_when_present()
    {
        var row = SectionTable.Load(Path.Combine(Fx.Attr, "H-BEAM.dat")).First(r => r.Spec.StartsWith("H400x200", StringComparison.Ordinal));
        var profile = Profile.FromRecord(row);
        var part = new ShapePart
        {
            Profile = profile,
            Length = 6000,
            Holes = [],
            Copes =
            [
                new FlangeCope(CopeCorner.TopStart, 180, 40, 35),
                new FlangeCope(CopeCorner.BottomStart, 180, 40, 35),
            ],
            Mark = "B-COPE",
            Quantity = 1,
        };
        var d = PartDetail.Generate(part);
        Assert.True(d.Meta["copes"]!.GetValue<int>() >= 2);
        Assert.Contains(d.Entities, e => e.Type is "polyline" or "lwpolyline");
    }

    [Fact]
    public void Weld_and_note_leaders_stretch_to_min_paper_length()
    {
        var d = new DrawPlan { Scale = 10 };
        Callouts.LeaderIfLongEnough(d, Layers.Weld, (100, 100), (100.5, 100.5));
        var shortLeader = Assert.Single(d.Entities, e => e.Type == "leader");
        var pts = shortLeader.Spec["points"]!.AsArray();
        var x0 = pts[0]![0]!.GetValue<double>();
        var y0 = pts[0]![1]!.GetValue<double>();
        var x1 = pts[1]![0]!.GetValue<double>();
        var y1 = pts[1]![1]!.GetValue<double>();
        var len = Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)));
        Assert.InRange(len, (Callouts.MinLeaderPaper * d.Scale) - 0.05, (Callouts.MinLeaderPaper * d.Scale) + 0.05);
    }

    [LegacyAssetFact]
    public void Hs_bom_tool_writes_csv_and_json_exports()
    {
        var ws = Fx.Ws.Value;
        var p = Demo.Sample();
        ws.Save(p);
        var dir = Path.Combine(Path.GetTempPath(), "hs-steel-d4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tools = new HsTools(ws);
            var csv = Path.Combine(dir, "bom.csv");
            var json = Path.Combine(dir, "bom.json");
            var raw = tools.Bom(p.Name, csv_path: csv, json_path: json);
            Assert.True(File.Exists(csv));
            Assert.True(File.Exists(json));
            Assert.Contains("조립목록", File.ReadAllText(csv), StringComparison.Ordinal);
            var node = JsonNode.Parse(raw)!;
            Assert.True(node["total_kg"]!.GetValue<double>() > 0);
            Assert.NotNull(node["exports"]!["csv"]);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ParseKinds_accepts_elevation_alias()
    {
        Assert.Equal(DrawingSet.Kinds.Plan, HsTools.ParseKinds(["elevation"]));
        Assert.Equal(DrawingSet.Kinds.Plan, HsTools.ParseKinds(["입면"]));
    }
}
