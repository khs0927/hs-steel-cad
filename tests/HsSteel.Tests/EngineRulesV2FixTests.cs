using ACadSharp.Entities;
using ACadSharp.IO;
using HsSteel.Assets;
using HsSteel.Domain;
using HsSteel.Drafting;
using HsSteel.Modeling;

namespace HsSteel.Tests;

/// <summary>
/// Engine fixes from the Rules v2 cross-check (docs/RULES_CATALOG.md): HL-001 hole diameter, NUM-001 mark heads,
/// WT-002 fallback unit weight with root fillets, DFT-001 dimension text height.
/// </summary>
public sealed class EngineRulesV2FixTests
{
    // ---- HL-001: BoltPattern.HoleDia must follow Bolts.HoleFor (d+2 up to M22, d+3 from M24) ----

    [Theory]
    [InlineData(16, 18)]
    [InlineData(20, 22)]
    [InlineData(22, 24)]
    [InlineData(24, 27)]
    [InlineData(27, 30)]
    [InlineData(30, 33)]
    public void HL001_bolt_pattern_hole_is_d_plus_2_below_M24_and_d_plus_3_from_M24(double bolt, double hole)
    {
        var p = BoltPattern.Parse($"9TX40+2A30+40Y40+3A60+40D{bolt}");
        Assert.Equal(hole, p.HoleDia);
        Assert.Equal(Bolts.HoleFor(bolt), p.HoleDia);
    }

    [Fact]
    public void HL001_pattern_without_bolt_has_no_hole() =>
        Assert.Equal(0, BoltPattern.Parse("9TX40+2A30+40").HoleDia);

    // ---- NUM-001: assembly mark heads = legacy Numbering.dat (M83-*-HD-BOX) ----

    [Fact]
    public void NUM001_default_prefixes_match_numbering_dat_heads()
    {
        var expected = new Dictionary<AssemblyType, string>
        {
            [AssemblyType.Column] = "C", [AssemblyType.SubColumn] = "C", [AssemblyType.Post] = "C",
            [AssemblyType.Rafter] = "G", [AssemblyType.Truss] = "G", [AssemblyType.CraneGirder] = "G", [AssemblyType.Girder] = "G",
            [AssemblyType.Beam] = "B", [AssemblyType.Brace] = "R", [AssemblyType.Purlin] = "PU", [AssemblyType.Girth] = "GT",
            [AssemblyType.Stair] = "S", [AssemblyType.HandRail] = "H", [AssemblyType.Other] = "X",
        };
        foreach (var (t, head) in expected)
        {
            Assert.Equal(head, AssemblyTypes.Prefix(t));
        }

        Assert.Equal("EB", AssemblyTypes.Prefix(AssemblyType.Embed)); // no M83 head: legacy M80 EMBED head EB01 (DECISIONS_BOLT_MARKS.md)
    }

    [Fact]
    public void NUM001_heads_are_read_from_numbering_settings_and_override_defaults()
    {
        var dat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["M83-COLUMN-HD-BOX"] = "K001",
            ["M83-BRACE--HD-BOX"] = "VB001",
            ["M83-BEAM---HD-BOX"] = "",
        };
        var heads = AssemblyTypes.HeadsFrom(dat);
        Assert.Equal("K", heads[AssemblyType.Column]);
        Assert.Equal("VB", heads[AssemblyType.Brace]);
        Assert.False(heads.ContainsKey(AssemblyType.Beam));
        Assert.Equal("K", AssemblyTypes.Prefix(AssemblyType.Column, heads));
        Assert.Equal("B", AssemblyTypes.Prefix(AssemblyType.Beam, heads));
        Assert.Equal("C", AssemblyTypes.Prefix(AssemblyType.Post, null));

        var rules = DetailRules.From(dat);
        Assert.Equal("K", rules.MarkHeads![AssemblyType.Column]);
        Assert.Null(DetailRules.From(new Dictionary<string, string>()).MarkHeads);
    }

    [Fact]
    public void NUM001_types_sharing_a_head_are_numbered_without_collisions()
    {
        var p = new Project
        {
            Name = "NUM001",
            Members =
            [
                new MemberDef("C1", AssemblyType.Column, "H300x300x10x15", new V3(0, 0, 0), new V3(0, 0, 4000)),
                new MemberDef("SC1", AssemblyType.SubColumn, "H200x200x8x12", new V3(6000, 0, 0), new V3(6000, 0, 3000)),
                new MemberDef("P1", AssemblyType.Post, "H150x150x7x10", new V3(12000, 0, 0), new V3(12000, 0, 2000)),
                new MemberDef("G1", AssemblyType.Girder, "H400x200x8x13", new V3(0, 0, 4000), new V3(6000, 0, 4000)),
                new MemberDef("R1", AssemblyType.Rafter, "H350x175x7x11", new V3(0, 6000, 4000), new V3(6000, 6000, 5000)),
                new MemberDef("BR1", AssemblyType.Brace, "L90x90x6", new V3(0, 0, 0), new V3(6000, 0, 4000)),
            ],
        };
        var r = new ModelBuilder(SectionCatalog.Empty, SpliceStandards.Empty).Build(p);
        var marks = r.Assemblies.Select(a => a.Mark).ToList();
        Assert.Equal(marks.Count, marks.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["C001", "C002", "C003"], new[] { "C1", "SC1", "P1" }.Select(id => r.MemberMarks[id]).Order(StringComparer.Ordinal));
        Assert.Equal(["G001", "G002"], new[] { "G1", "R1" }.Select(id => r.MemberMarks[id]).Order(StringComparer.Ordinal));
        Assert.Equal("R001", r.MemberMarks["BR1"]);
    }

    [Fact]
    public void NUM001_project_heads_flow_into_marks()
    {
        var p = new Project
        {
            Name = "NUM001-CFG",
            Rules = new DetailRules { MarkHeads = new Dictionary<AssemblyType, string> { [AssemblyType.Beam] = "SB" } },
            Members = [new MemberDef("B1", AssemblyType.Beam, "H400x200x8x13", new V3(0, 0, 4000), new V3(6000, 0, 4000))],
        };
        var r = new ModelBuilder(SectionCatalog.Empty, SpliceStandards.Empty).Build(p);
        Assert.Equal("SB001", r.MemberMarks["B1"]);
    }

    [LegacyAssetFact]
    public void NUM001_original_numbering_dat_heads_equal_engine_defaults()
    {
        var path = Path.Combine(Fx.Legacy, "새공사-설치용", "attributes", "Numbering.dat");
        if (!File.Exists(path))
        {
            return; // new-project template set not installed
        }

        var numbering = ProjectSettings.Load(path);
        var heads = AssemblyTypes.HeadsFrom(numbering);
        Assert.Equal(AssemblyTypes.NumberingKeys.Count - 1, heads.Count); // every M83 head except the engine-only EMBED key
        Assert.False(heads.ContainsKey(AssemblyType.Embed));
        Assert.All(heads, kv => Assert.Equal(kv.Value, AssemblyTypes.Prefix(kv.Key)));
        Assert.Equal(3, AssemblyTypes.DigitsFrom(numbering)); // C001, G001 ... (NUM-002)
    }

    // ---- WT-002: fallback unit weight includes the root fillet (4−π)·r² ----

    [Theory]
    [InlineData(400, 200, 8, 13, 16, 66.0)] // H400x200x8x13 (H-BEAM.dat 66.0)
    [InlineData(400, 200, 8, 13, 8, 64.7)] // BH400x200x8x13, r = tw
    [InlineData(100, 100, 6, 8, 10, 17.2)] // H100x100x6x8 (H-BEAM.dat 17.2)
    public void WT002_h_unit_weight_formula(double h, double b, double tw, double tf, double r, double kgm) =>
        Assert.Equal(kgm, Profile.HUnitWeight(h, b, tw, tf, r));

    [Fact]
    public void WT002_parsed_built_up_section_uses_r_equal_tw()
    {
        var bh = Profile.Parse("BH400x200x8x13");
        Assert.Equal(64.7, bh.UnitWeight); // plates alone: 64.3
        Assert.Equal(0, bh.RootRadius); // welded: sharp outline, fillet term only in the weight
    }

    [Fact]
    public void WT002_parsed_rolled_h_includes_fillets()
    {
        Assert.Equal(66.0, Profile.Parse("H400x200x8x13", 16).UnitWeight);
        var est = Profile.Parse("H400x200x8x13");
        Assert.True(est.RootRadius > 0);
        Assert.True(est.UnitWeight > Profile.HUnitWeight(400, 200, 8, 13, 0));
    }

    [Fact]
    public void WT002_parsed_angle_and_channel_add_root_fillet_area()
    {
        static double Sharp(string spec, double r) => Profile.Area(Profile.Parse(spec, r));
        var oneFillet = (1 - (Math.PI / 4)) * 8 * 8;
        Assert.InRange(Sharp("L90x90x6", 8) - Sharp("L90x90x6", 0), 0.97 * oneFillet, 1.03 * oneFillet);
        Assert.InRange(Sharp("[150x75x6.5x10", 8) - Sharp("[150x75x6.5x10", 0), 2 * 0.97 * oneFillet, 2 * 1.03 * oneFillet);
        Assert.True(Profile.Parse("L90x90x6").RootRadius > 0);
        Assert.True(Profile.Parse("[150x75x6.5x10").RootRadius > 0);
    }

    [LegacyAssetFact]
    public void WT002_fallback_reproduces_every_h_and_bh_table_weight()
    {
        var bad = new List<string>();
        var n = 0;
        foreach (var family in new[] { "H-BEAM", "BH-BEAM" })
        {
            foreach (var row in SectionTable.Load(Path.Combine(Fx.Attr, family + ".dat")))
            {
                var table = Profile.FromRecord(row);
                var parsed = family == "H-BEAM" ? Profile.Parse(row.Spec, table.RootRadius) : Profile.Parse(row.Spec);
                n++;
                if (Math.Abs(parsed.UnitWeight - row.UnitWeight) > Math.Max(0.1, 0.005 * row.UnitWeight))
                {
                    bad.Add($"{row.Spec}: parsed {parsed.UnitWeight} vs table {row.UnitWeight}");
                }
            }
        }

        Assert.True(n >= 200, $"only {n} rows");
        Assert.True(bad.Count <= 1, string.Join("; ", bad)); // RULES_CATALOG: BH 158/159 with r = tw
    }

    [LegacyAssetFact]
    public void WT002_catalog_fallback_borrows_nearest_tabulated_root_radius()
    {
        var p = Fx.Ws.Value.Catalog.Resolve("H401x200x8x13");
        Assert.Equal(16, p.RootRadius); // nearest row H400x200x8x13 (r 16)
        Assert.Equal(Profile.HUnitWeight(401, 200, 8, 13, 16), p.UnitWeight);
    }

    // ---- DFT-001: dimension text height follows the template DIM-100 / Standard (3.4 on paper) ----

    [Fact]
    public void DFT001_dimension_text_height_is_template_3_4()
    {
        Assert.Equal(3.4, DetailRules.TemplateDimText);
        Assert.Equal(3.4, new DetailRules().DimTextHeight);
        Assert.Equal(DetailRules.TemplateDimText, AnnotationBoxes.DimTextPaper);
        Assert.Equal(170, AnnotationBoxes.DimTextHeight(50), 6); // DIM-100: 3.4 at DIMSCALE 50
        Assert.Equal(3.4 * 20, AnnotationBoxes.DimText(false, 0, 0, "1000", 20).H, 6);
    }

    [Fact]
    public void DFT001_dxf_dimension_text_uses_template_height()
    {
        var d = new DrawPlan { Scale = 50 };
        d.Dim(Layers.Dim, (0, 0), (3000, 0), (1500, -1000), 0);
        var path = Path.Combine(Path.GetTempPath(), $"hs_dft001_{Guid.NewGuid():N}.dxf");
        try
        {
            DxfExporter.Write([d], SheetFrame.A3Default, path);
            var doc = DxfReader.Read(path);
            var text = Assert.Single(doc.Entities.OfType<TextEntity>(), t => t.Value == "3000");
            Assert.Equal(3.4 * 50, text.Height, 6);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
