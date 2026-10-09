using HsSteel.Drafting;
using HsSteel.Mcp;

namespace HsSteel.Tests;

/// <summary>General (G) and anchor (F) sheets built from the original HS-STEEL blocks.</summary>
public class DocSheetsTests
{
    private static DrawingSet Full() =>
        DrawingSet.Generate(Demo.Sample(), Fx.Ws.Value.Catalog, Fx.Ws.Value.Splices, SheetFrame.A3Default, DrawingSet.Kinds.Full);

    [LegacyAssetFact]
    public void Default_kinds_are_unchanged()
    {
        var set = DrawingSet.Generate(Demo.Sample(), Fx.Ws.Value.Catalog, Fx.Ws.Value.Splices, SheetFrame.A3Default);
        Assert.DoesNotContain(set.Sheets, s => s.Number.StartsWith("G-", StringComparison.Ordinal) || s.Number.StartsWith("F-", StringComparison.Ordinal));
    }

    [LegacyAssetFact]
    public void Cover_is_G001_and_lists_every_sheet_number()
    {
        var set = Full();
        Assert.Equal("G-001", set.Sheets[0].Number);
        var texts = set.Sheets[0].Plan.Entities.Where(e => e.Type == "text").Select(e => e.Spec["text"]!.GetValue<string>()).ToHashSet();
        foreach (var s in set.Sheets)
        {
            Assert.Contains(s.Number, texts);
        }

        Assert.Equal(set.Sheets.Count, set.Sheets.Select(s => s.Number).Distinct().Count());
    }

    [LegacyAssetFact]
    public void Notes_and_tables_insert_the_original_blocks()
    {
        var inserts = Full().Sheets.SelectMany(s => s.Plan.Entities).Where(e => e.Type == "insert").Select(e => e.Spec["name"]!.GetValue<string>()).ToHashSet();
        Assert.Contains(HsBlocks.Notes, inserts);
        Assert.Contains(HsBlocks.FilletWeld, inserts);
        Assert.Contains(HsBlocks.BracketWeld, inserts);
        Assert.Contains(HsBlocks.BoltData, inserts);
        Assert.Contains(HsBlocks.HoleDia, inserts);
    }

    [LegacyAssetFact]
    public void Anchor_plan_draws_four_holes_per_base_plate()
    {
        var p = Demo.Sample();
        var bases = p.Connections.OfType<HsSteel.Modeling.BasePlateDef>().Count();
        var set = DrawingSet.Generate(p, Fx.Ws.Value.Catalog, Fx.Ws.Value.Splices, SheetFrame.A3Default, DrawingSet.Kinds.Anchor);
        var holes = set.Sheets.SelectMany(s => s.Plan.Entities).Count(e => e.Type == "circle" && e.Layer == Layers.Hole);
        Assert.True(bases > 0);
        Assert.Equal(bases * 4, holes);
    }

    [Theory]
    [InlineData(27, "HAS M24x290K")]
    [InlineData(23, "HAS M20x240K")]
    [InlineData(19, "HAS M16x190K")]
    public void Anchor_block_follows_hole_diameter(double hole, string block) => Assert.Equal(block, HsBlocks.Anchor(hole));

    [LegacyAssetFact]
    public void Zium_frame_is_inserted_at_sheet_scale_over_200()
    {
        var set = DrawingSet.Generate(Demo.Sample(), Fx.Ws.Value.Catalog, Fx.Ws.Value.Splices, SheetFrame.ZiumA3, DrawingSet.Kinds.Plan);
        foreach (var s in set.Sheets)
        {
            var frame = s.Plan.Entities.Single(e => e.Type == "insert" && e.Spec["name"]!.GetValue<string>() == "ZIUM_sheet_architect");
            Assert.Equal(s.Scale / 200, frame.Spec["scale"]!.GetValue<double>(), 6);
        }
    }

    [Fact]
    public void Workspace_default_frame_is_zium() => Assert.Equal("ZIUM_sheet_architect", new SheetFrameHolder().Frame.BlockName);

    private sealed class SheetFrameHolder
    {
        public SheetFrame Frame { get; } = Fx.Ws.Value.Frame;
    }

    [Fact]
    public void ParseKinds_accepts_doc_kinds()
    {
        Assert.Equal(DrawingSet.Kinds.Cover | DrawingSet.Kinds.Anchor, HsTools.ParseKinds(["표지", "anchor"]));
        Assert.Equal(DrawingSet.Kinds.Full, HsTools.ParseKinds(["full"]));
    }
}
