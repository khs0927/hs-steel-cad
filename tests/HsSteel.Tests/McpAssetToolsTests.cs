using System.Text;
using System.Text.Json.Nodes;
using HsSteel.Knowledge;
using HsSteel.Mcp;
using ModelContextProtocol;

namespace HsSteel.Tests;

/// <summary>Builds a small knowledge DB (sections + one block) in a temp "legacy tree" and exercises the asset MCP tools.</summary>
public sealed class AssetDbFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "hs-mcp-assets-" + Guid.NewGuid().ToString("N"));

    public AssetOptions Options { get; }

    public AssetDbFixture()
    {
        var attr = Path.Combine(Root, "HSSTEEL", "attributes");
        var block = Path.Combine(Root, "HSSTEEL", "block");
        Directory.CreateDirectory(attr);
        Directory.CreateDirectory(block);
        File.WriteAllText(
            Path.Combine(attr, "H-BEAM.dat"),
            "규격  M1  M2  M3  M4  M5  M6  M7  단중 PAINT 색상\n"
            + "H100x100x6x8  H  100  100  6  8  10  0  17.2  0.588  32  H-BEAM\n"
            + "H125x125x6.5x9  H  125  125  6.5  9  10  0  23.8  0.737  32  H-BEAM\n"
            + "H150x75x5x7  H  150  75  5  7  8  0  14  0.59  32  H-BEAM\n",
            HsSteel.Assets.SectionTable.Cp949);
        File.WriteAllBytes(Path.Combine(block, "HTB.dwg"), [1, 2, 3]);
        var manifest = ManifestScanner.Scan(Root);
        var db = Path.Combine(Root, "out", "hs_assets.db");
        new KnowledgeDbBuilder(manifest)
            .IngestSections(Root)
            .IngestBlocks([new BlockRecord("HTB-M20", "HSSTEEL/block/HTB.dwg", 0, 0, 0, -10, -10, 10, 10, 3, "<svg/>",
                [new BlockAttributeRecord("SIZE", "Bolt size", "M20")], ["HS-BOLT"])])
            .Build(db);
        Options = new AssetOptions(db, Root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class McpAssetToolsTests(AssetDbFixture fx) : IClassFixture<AssetDbFixture>
{
    private AssetTools Tools() => new(Fx.Ws.Value, fx.Options);

    [Fact]
    public void Search_finds_exact_section_with_source()
    {
        var hits = JsonNode.Parse(Tools().AssetSearch("H125x125x6.5x9", "section"))!.AsArray();
        Assert.NotEmpty(hits);
        Assert.Equal("H-BEAM/H125x125x6.5x9", hits[0]!["key"]!.GetValue<string>());
        Assert.Equal("HSSTEEL/attributes/H-BEAM.dat", hits[0]!["source_file"]!.GetValue<string>());
    }

    [Fact]
    public void Get_section_has_provenance_and_block_has_attributes()
    {
        var s = JsonNode.Parse(Tools().AssetGet("section", "H-BEAM/H100x100x6x8"))!;
        Assert.Equal(64, s["provenance"]!["sha256"]!.GetValue<string>().Length);
        Assert.Equal("HSSTEEL/attributes/H-BEAM.dat", s["provenance"]!["source_file"]!.GetValue<string>());

        var b = JsonNode.Parse(Tools().AssetGet("block", "HTB-M20", include_preview: true))!;
        Assert.Equal("SIZE", b["block"]!["attributes"]![0]!["tag"]!.GetValue<string>());
        Assert.NotNull(b["preview_svg"]);
        Assert.Throws<McpException>(() => Tools().AssetGet("section", "nope"));
    }

    [Fact]
    public void Neighbors_of_section_include_family()
    {
        var r = JsonNode.Parse(Tools().GraphNeighbors("section", "H-BEAM/H100x100x6x8"))!;
        Assert.Contains(r["neighbors"]!.AsArray(), n => n!["kind"]!.GetValue<string>() == "family" && n["rel"]!.GetValue<string>() == "family");
    }

    [Fact]
    public void Block_insert_plan_matches_power_cad_shapes()
    {
        var plan = JsonNode.Parse(Tools().BlockInsertPlan("htb-m20", 100, 200, 90, 2, new Dictionary<string, string> { ["SIZE"] = "M22", ["BAD"] = "x" }))!;
        Assert.Equal("HTB-M20", plan["import"]!["name"]!.GetValue<string>());
        Assert.Equal(Path.Combine(fx.Root, "HSSTEEL", "block", "HTB.dwg"), plan["import"]!["path"]!.GetValue<string>());
        var c = plan["create"]!;
        Assert.Equal("insert", c["type"]!.GetValue<string>());
        Assert.Equal(100, c["position"]![0]!.GetValue<double>());
        Assert.Equal(90, c["rotation"]!.GetValue<double>());
        Assert.Equal("M22", c["attributes"]!["SIZE"]!.GetValue<string>());
        Assert.Single(plan["warnings"]!.AsArray());
        Assert.Throws<McpException>(() => Tools().BlockInsertPlan("NOPE", 0, 0));
    }

    [Fact]
    public void Resources_report_stats_and_sections()
    {
        var res = new AssetResources(fx.Options);
        var st = JsonNode.Parse(res.Stats())!;
        Assert.Equal(3, st["tables"]!["section"]!.GetValue<long>());
        var sec = JsonNode.Parse(res.Sections("H-BEAM"))!;
        Assert.Equal(3, sec["count"]!.GetValue<int>());
    }

    [Fact]
    public void Drawings_to_powercad_tags_and_chunks()
    {
        if (!Fx.HasLegacy)
        {
            return;
        }

        var ws = Fx.Ws.Value;
        ws.Save(Demo.Sample());
        var tools = new AssetTools(ws, fx.Options);
        var r = JsonNode.Parse(tools.DrawingsToPowerCad("SAMPLE-FRAME"))!;
        var payloads = r["payloads"]!.AsArray();
        Assert.Equal(r["payload_count"]!.GetValue<int>(), payloads.Count);
        Assert.Equal(r["total_entities"]!.GetValue<int>(), payloads.Sum(p => p!["entities"]!.AsArray().Count));
        Assert.All(payloads, p => Assert.InRange(p!["entities"]!.AsArray().Count, 1, AssetTools.MaxEntitiesPerCall));
        Assert.Equal("HS-STEEL", payloads[0]!["xdata_app"]!.GetValue<string>());
        Assert.NotEmpty(payloads[0]!["layers"]!.AsArray());

        var tagged = payloads.SelectMany(p => p!["entities"]!.AsArray()).Select(e => e!["hs"]!.AsObject()).ToList();
        Assert.All(tagged, t => Assert.NotNull(t["sheet"]));
        Assert.Contains(tagged, t => t["kind"]?.GetValue<string>() == "assembly" && t["spec"] is not null && t["length"] is not null && t["assembly"] is not null);
        Assert.Contains(tagged, t => t["kind"]?.GetValue<string>() == "part" && t["spec"] is not null && t["length"] is not null);

        var first = r["sheets"]![0]!["dwg_no"]!.GetValue<string>();
        var one = JsonNode.Parse(tools.DrawingsToPowerCad("SAMPLE-FRAME", first))!;
        Assert.Single(one["sheets"]!.AsArray());
        Assert.Throws<McpException>(() => tools.DrawingsToPowerCad("SAMPLE-FRAME", "Z-999"));
    }
}
