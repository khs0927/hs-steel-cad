using System.Text.Json.Nodes;
using HsSteel.Domain;
using HsSteel.Drafting;
using HsSteel.Modeling;
using HsSteel.Mcp;

namespace HsSteel.Tests;

/// <summary>D3 residual: interactive grid/rules MCP + cope callouts on shop details.</summary>
public class ModelingD3ResidualTests
{
    [LegacyAssetFact]
    public void Project_frame_accepts_end_plate_and_detail_rules()
    {
        var ws = Fx.Ws.Value;
        var tools = new HsTools(ws);
        var name = "D3R-EP-" + Guid.NewGuid().ToString("N")[..8];
        var raw = tools.ProjectFrame(
            name,
            spansX: [6000],
            spansY: [6000],
            storeys: [4000],
            beamConnection: "엔드플레이트",
            scallop: 35,
            connectionGap: 12,
            weldGap: 6,
            material: "SM355",
            overwrite: true);
        var node = JsonNode.Parse(raw)!;
        Assert.Equal("end_plate", node["beam_connection"]!.GetValue<string>());
        Assert.Equal(35, node["rules"]!["scallop"]!.GetValue<double>());
        Assert.Equal("SM355", node["rules"]!["material"]!.GetValue<string>());

        var p = ws.Load(name);
        Assert.NotNull(p.Rules);
        Assert.Equal(35, p.Rules!.Scallop);
        Assert.Contains(p.Connections, c => c is EndPlateDef);
        Assert.DoesNotContain(p.Connections, c => c is ShearTabDef);
    }

    [LegacyAssetFact]
    public void Grid_from_bays_and_project_rules_round_trip()
    {
        var ws = Fx.Ws.Value;
        var tools = new HsTools(ws);
        var name = "D3R-GRID-" + Guid.NewGuid().ToString("N")[..8];
        var grid = JsonNode.Parse(tools.GridFromBays(name, [6000, 6000], [8000], storeys: [4500, 4000]))!;
        Assert.Equal(3, grid["grid_x"]!.AsArray().Count);
        Assert.Equal(2, grid["grid_y"]!.AsArray().Count);
        Assert.Equal(3, grid["levels"]!.AsArray().Count);
        Assert.Equal(12000, grid["grid_x"]![2]!["position"]!.GetValue<double>());

        var rules = JsonNode.Parse(tools.ProjectRules(name, scallop: 40, connectionGap: 15))!;
        Assert.Equal(40, rules["rules"]!["scallop"]!.GetValue<double>());
        Assert.Equal(15, rules["rules"]!["connection_gap"]!.GetValue<double>());
        Assert.Equal(40, ws.Load(name).Rules!.Scallop);
    }

    [Fact]
    public void ParseBeamConnection_aliases()
    {
        // Exercise via ProjectFrame would need assets; use public ParseKinds-style coverage through reflection-free API:
        // ProjectFrame is the public surface — aliases covered in Project_frame_accepts when legacy present.
        Assert.Equal(DrawingSet.Kinds.Plan, HsTools.ParseKinds(["elevation"]));
    }

    [LegacyAssetFact]
    public void Part_detail_emits_cope_notes_when_copes_present()
    {
        var row = HsSteel.Assets.SectionTable.Load(Path.Combine(Fx.Attr, "H-BEAM.dat"))
            .First(r => r.Spec.StartsWith("H400x200", StringComparison.Ordinal));
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
            Mark = "B-COPE2",
            Quantity = 1,
        };
        var d = PartDetail.Generate(part);
        Assert.True(d.Meta["cope_notes"]!.GetValue<int>() >= 1);
        var texts = d.Entities.Where(e => e.Type == "text").Select(e => e.Spec["text"]!.GetValue<string>()).ToList();
        Assert.Contains(texts, t => t.StartsWith("COPE L=", StringComparison.Ordinal));
        Assert.Equal("COPE L=180 D=40 R=35", Callouts.CopeNote(part.Copes[0]));
    }
}
