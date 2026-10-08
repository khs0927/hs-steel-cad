using System.Text.Json.Nodes;
using HsSteel.Assets;
using HsSteel.Domain;
using HsSteel.Drafting;
using HsSteel.Mcp;
using HsSteel.Modeling;
using ModelContextProtocol;

namespace HsSteel.Tests;

/// <summary>
/// Project-level standard options (docs/STANDARDS_RESEARCH.md): bolt length table, hole rule, mark scheme/format.
/// Defaults must equal the engine's pre-option behaviour except where noted (hole M24+ in ModelBuilder: d+3, HL-001).
/// </summary>
public sealed class StandardOptionsTests
{
    private static ModelResult Build(Project p) => new ModelBuilder(SectionCatalog.Empty, SpliceStandards.Empty).Build(p);

    /// <summary>Column + one shear-tab girder with bolt size <paramref name="bolt"/> (beam web tw 8, plate 9 → grip 17).</summary>
    private static Project ShearTab(DetailRules? rules, int bolt = 20) => new()
    {
        Name = "OPT",
        Rules = rules,
        Members =
        [
            new MemberDef("C1", AssemblyType.Column, "H300x300x10x15", new V3(0, 0, 0), new V3(0, 0, 4000)),
            new MemberDef("G1", AssemblyType.Girder, "H400x200x8x13", new V3(0, 0, 4000), new V3(6000, 0, 4000)),
        ],
        Connections = [new ShearTabDef("T1", "G1", MemberEnd.Start, "C1", 0, bolt)],
    };

    private static BoltSet GirderBolts(ModelResult r) => r.Assemblies.Single(a => a.Type == AssemblyType.Girder).Bolts.Single();

    // ---- defaults ----

    [Fact]
    public void Defaults_are_kcs_standard_legacy_plain_and_survive_json()
    {
        var d = new DetailRules();
        Assert.Equal(("kcs", "standard", "legacy", "plain"), (d.BoltLengthTable, d.HoleRule, d.MarkScheme, d.MarkFormat));
        var p = ShearTab(new DetailRules { BoltLengthTable = "ts_one_washer", HoleRule = "oversize", MarkScheme = "alt", MarkFormat = "floor_prefix" });
        var back = Project.FromJson(p.ToJson()).Rules!;
        Assert.Equal(("ts_one_washer", "oversize", "alt", "floor_prefix"), (back.BoltLengthTable, back.HoleRule, back.MarkScheme, back.MarkFormat));
        Assert.Equal("kcs", StandardOptions.NormalizeBoltLengthTable("garbage"));
        Assert.Equal("kcs", StandardOptions.NormalizeBoltLengthTable(null));
    }

    // ---- bolt length ----

    [Theory]
    [InlineData("kcs", "F10T", 16, 30)]
    [InlineData("kcs", "F10T", 20, 35)]
    [InlineData("kcs", "F10T", 22, 40)]
    [InlineData("kcs", "F10T", 24, 45)]
    [InlineData("kcs", "F10T", 27, 50)]
    [InlineData("kcs", "F10T", 30, 55)]
    [InlineData("kcs", "S10T", 20, 35)]
    [InlineData("ts_one_washer", "S10T", 16, 25)]
    [InlineData("ts_one_washer", "F10T", 20, 30)]
    [InlineData("ts_one_washer", "S10T", 22, 35)]
    [InlineData("ts_one_washer", "S10T", 24, 40)]
    [InlineData("by_bolt_set", "S10T", 20, 30)]
    [InlineData("by_bolt_set", "F10T", 20, 35)]
    [InlineData("by_bolt_set", "S10T", 22, 35)]
    [InlineData("by_bolt_set", "F10T", 24, 45)]
    public void Add_length_per_table(string table, string grade, double dia, double add) =>
        Assert.Equal(add, StandardOptions.AddLength(table, grade, dia));

    [Theory]
    [InlineData("kcs", "S10T", 20, 17, 55)]           // 17 + 35 = 52 -> 55
    [InlineData("ts_one_washer", "S10T", 20, 17, 50)] // 17 + 30 = 47 -> 50
    [InlineData("kcs", "F10T", 20, 40, 75)]           // 40 + 35 = 75 exactly (no extra step)
    [InlineData("kcs", "F10T", 20, 41, 80)]
    [InlineData("by_bolt_set", "F10T", 22, 36, 80)]   // 36 + 40 = 76 -> 80
    [InlineData("by_bolt_set", "S10T", 22, 36, 75)]   // 36 + 35 = 71 -> 75
    public void Bolt_length_rounds_up_to_5(string table, string grade, double dia, double grip, double expected) =>
        Assert.Equal(expected, StandardOptions.BoltLength(table, grade, dia, grip));

    [Fact]
    public void Builder_computes_grip_based_length_and_label()
    {
        var kcs = GirderBolts(Build(ShearTab(null))); // default = kcs, TS set, grip 8 + 9 = 17
        Assert.Equal(("TS M20", 55d, "TS M20x55"), (kcs.Name, kcs.Length, kcs.Label));
        var ts = GirderBolts(Build(ShearTab(new DetailRules { BoltLengthTable = "ts_one_washer" })));
        Assert.Equal(50d, ts.Length);
        var bySet = GirderBolts(Build(ShearTab(new DetailRules { BoltLengthTable = "by_bolt_set" })));
        Assert.Equal(50d, bySet.Length); // TS bolts -> ts_one_washer
        Assert.Equal(kcs.Count, ts.Count);
    }

    [Fact]
    public void Bom_bolt_table_shows_length()
    {
        var bom = BomTable.From(Build(ShearTab(null)));
        Assert.Contains(bom.Bolts, b => b.Name == "TS M20x55");
    }

    // ---- hole rule ----

    [Theory]
    [InlineData("standard", 16, 18)]
    [InlineData("standard", 20, 22)]
    [InlineData("standard", 22, 24)]
    [InlineData("standard", 24, 27)]
    [InlineData("standard", 27, 30)]
    [InlineData("standard", 30, 33)]
    [InlineData("oversize", 16, 20)]
    [InlineData("oversize", 20, 24)]
    [InlineData("oversize", 22, 28)] // inferred
    [InlineData("oversize", 24, 30)]
    [InlineData("oversize", 27, 35)] // inferred
    [InlineData("oversize", 30, 38)]
    [InlineData("legacy", 16, 18)]
    [InlineData("legacy", 24, 26)]
    [InlineData("legacy", 30, 32)]
    public void Hole_diameter_per_rule(string rule, double dia, double hole) =>
        Assert.Equal(hole, StandardOptions.HoleFor(dia, rule));

    [Fact]
    public void Standard_rule_agrees_with_Bolts_HoleFor_and_BoltPattern()
    {
        foreach (var d in new double[] { 12, 16, 20, 22, 24, 27, 30 })
        {
            Assert.Equal(Bolts.HoleFor(d), StandardOptions.HoleFor(d, "standard"));
            Assert.Equal(Bolts.HoleFor(d), new BoltPattern([], null, null, d).HoleDia);
            Assert.Equal(Bolts.HoleFor(d), new DetailRules().HoleFor(d));
        }
    }

    [Theory]
    [InlineData("standard", 27)]
    [InlineData("oversize", 30)]
    [InlineData("legacy", 26)]
    public void Builder_uses_the_rule_for_m24_holes(string rule, double hole)
    {
        var r = Build(ShearTab(new DetailRules { HoleRule = rule }, 24));
        var g = r.ShapeParts.Single(s => s.Holes.Count > 0 && s.Profile.Spec.StartsWith("H400", StringComparison.Ordinal));
        Assert.All(g.Holes, h => Assert.Equal(hole, h.Dia));
        var plate = r.PlateParts.Single(pl => pl.Holes.Count > 0);
        Assert.All(plate.Holes, h => Assert.Equal(hole, h.Dia));
    }

    [Fact]
    public void M20_holes_are_22_under_default_as_before()
    {
        var g = Build(ShearTab(null)).ShapeParts.Single(s => s.Holes.Count > 0);
        Assert.All(g.Holes, h => Assert.Equal(22, h.Dia));
    }

    // ---- marks ----

    private static Project Marks(DetailRules? rules) => new()
    {
        Name = "MK",
        Rules = rules,
        Levels = [new Level("BASE", 0), new Level("2F", 4000), new Level("3F", 8000)],
        Members =
        [
            new MemberDef("C1", AssemblyType.Column, "H300x300x10x15", new V3(0, 0, 0), new V3(0, 0, 4000)),
            new MemberDef("C2", AssemblyType.Column, "H300x300x10x15", new V3(6000, 0, 4000), new V3(6000, 0, 8000)),
            new MemberDef("G1", AssemblyType.Girder, "H400x200x8x13", new V3(0, 0, 4000), new V3(6000, 0, 4000)),
            new MemberDef("BR1", AssemblyType.Brace, "L90x90x6", new V3(0, 0, 0), new V3(6000, 0, 4000)),
            new MemberDef("ST1", AssemblyType.Stair, "H200x100x5.5x8", new V3(0, 3000, 0), new V3(2000, 3000, 2000)),
            new MemberDef("HR1", AssemblyType.HandRail, "L50x50x4", new V3(0, 4000, 0), new V3(2000, 4000, 2000)),
            new MemberDef("T1", AssemblyType.Truss, "H300x150x6.5x9", new V3(0, 6000, 8000), new V3(6000, 6000, 8000)),
        ],
    };

    [Fact]
    public void Legacy_plain_is_the_default_and_matches_numbering_dat_heads()
    {
        var r = Build(Marks(null));
        Assert.Equal("C1", r.MemberMarks["C1"]);
        Assert.Equal("C1", r.MemberMarks["C2"]); // identical assembly shares the mark
        Assert.Equal("G1", r.MemberMarks["G1"]);
        Assert.Equal("G2", r.MemberMarks["T1"]);
        Assert.Equal("R1", r.MemberMarks["BR1"]);
        Assert.Equal("S1", r.MemberMarks["ST1"]);
        Assert.Equal("H1", r.MemberMarks["HR1"]);
    }

    [Fact]
    public void Alt_scheme_uses_alt_heads()
    {
        var r = Build(Marks(new DetailRules { MarkScheme = "alt" }));
        Assert.Equal("V1", r.MemberMarks["BR1"]);
        Assert.Equal("ST1", r.MemberMarks["ST1"]);
        Assert.Equal("HR1", r.MemberMarks["HR1"]);
        Assert.Equal("T1", r.MemberMarks["T1"]);
        Assert.Equal("G1", r.MemberMarks["G1"]);
        Assert.Equal("C1", r.MemberMarks["C1"]);
        Assert.Equal("AB", StandardOptions.AltPrefix(AssemblyType.Embed));
    }

    [Fact]
    public void Alt_scheme_wins_over_numbering_dat_heads_but_legacy_honours_them()
    {
        var r = Build(Marks(new DetailRules { MarkScheme = "alt", MarkHeads = new Dictionary<AssemblyType, string> { [AssemblyType.Brace] = "R" } }));
        Assert.Equal("V1", r.MemberMarks["BR1"]);
        var legacy = Build(Marks(new DetailRules { MarkHeads = new Dictionary<AssemblyType, string> { [AssemblyType.Brace] = "BX" } }));
        Assert.Equal("BX1", legacy.MemberMarks["BR1"]);
    }

    [Fact]
    public void Floor_prefix_format_puts_the_floor_in_front()
    {
        var r = Build(Marks(new DetailRules { MarkFormat = "floor_prefix" }));
        Assert.Equal("1C1", r.MemberMarks["C1"]); // column standing on BASE (floor 1)
        Assert.Equal("2C1", r.MemberMarks["C2"]); // column standing on 2F
        Assert.Equal("2G1", r.MemberMarks["G1"]);
        Assert.Equal("3G1", r.MemberMarks["T1"]);
        Assert.Equal("1R1", r.MemberMarks["BR1"]);
        var marks = r.Assemblies.Select(a => a.Mark).ToList();
        Assert.Equal(marks.Count, marks.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Floor_tag_rules()
    {
        (string, double)[] lv = [("BASE", 0), ("2F", 4500), ("3F", 8500)];
        Assert.Equal("1", StandardOptions.FloorTag(lv, 0));
        Assert.Equal("2", StandardOptions.FloorTag(lv, 4500));
        Assert.Equal("2", StandardOptions.FloorTag(lv, 6000));
        Assert.Equal("3", StandardOptions.FloorTag(lv, 8500));
        Assert.Equal("1", StandardOptions.FloorTag([], 5000));
        Assert.Equal("2", StandardOptions.FloorTag([("GL", 0), ("MEZ", 3000)], 3000)); // no digits -> rank
    }

    [Fact]
    public void Same_input_gives_identical_marks_and_bolts()
    {
        var rules = new DetailRules { MarkScheme = "alt", MarkFormat = "floor_prefix", BoltLengthTable = "by_bolt_set", HoleRule = "oversize" };
        var a = Build(Marks(rules));
        var b = Build(Marks(rules));
        Assert.Equal(a.Assemblies.Select(x => x.Mark + x.Signature), b.Assemblies.Select(x => x.Mark + x.Signature));
    }

    // ---- MCP ----

    private static HsTools NewTools() => new(Workspace.Create(
        Path.Combine(Path.GetTempPath(), "hs-no-assets-" + Guid.NewGuid().ToString("N")),
        Path.Combine(Path.GetTempPath(), "hs-ws-" + Guid.NewGuid().ToString("N"))));

    [Fact]
    public void Mcp_options_get_set_and_validate()
    {
        var t = NewTools();
        t.ProjectNew("P1");
        var get = JsonNode.Parse(t.ProjectOptions("P1"))!;
        Assert.False(get["changed"]!.GetValue<bool>());
        Assert.Equal("kcs", get["options"]!["bolt_length_table"]!.GetValue<string>());
        Assert.Equal("legacy", get["options"]!["mark_scheme"]!.GetValue<string>());

        var set = JsonNode.Parse(t.ProjectOptions("P1", boltLengthTable: "by_bolt_set", holeRule: "oversize", markScheme: "alt", markFormat: "floor_prefix"))!;
        Assert.True(set["changed"]!.GetValue<bool>());
        var again = JsonNode.Parse(t.ProjectOptions("P1"))!;
        Assert.Equal("by_bolt_set", again["options"]!["bolt_length_table"]!.GetValue<string>());
        Assert.Equal("floor_prefix", again["options"]!["mark_format"]!.GetValue<string>());
        Assert.Contains("\"hole_rule\": \"oversize\"", t.ProjectGet("P1"));

        t.ProjectOptions("P1", holeRule: "legacy"); // partial update keeps the others
        var partial = JsonNode.Parse(t.ProjectOptions("P1"))!;
        Assert.Equal("legacy", partial["options"]!["hole_rule"]!.GetValue<string>());
        Assert.Equal("alt", partial["options"]!["mark_scheme"]!.GetValue<string>());

        Assert.Throws<McpException>(() => t.ProjectOptions("P1", holeRule: "huge"));
    }

    [Fact]
    public void Mcp_new_and_frame_accept_options()
    {
        var t = NewTools();
        t.ProjectNew("N1", boltLengthTable: "ts_one_washer", markScheme: "alt");
        var n = JsonNode.Parse(t.ProjectOptions("N1"))!["options"]!;
        Assert.Equal("ts_one_washer", n["bolt_length_table"]!.GetValue<string>());
        Assert.Equal("alt", n["mark_scheme"]!.GetValue<string>());
        Assert.Equal("standard", n["hole_rule"]!.GetValue<string>());

        var s = JsonNode.Parse(t.ProjectFrame("F1", [6000], [6000], [4000], holeRule: "oversize", markFormat: "floor_prefix"))!;
        Assert.Equal("oversize", s["rules"]!["options"]!["hole_rule"]!.GetValue<string>());
        Assert.Equal("floor_prefix", s["rules"]!["options"]!["mark_format"]!.GetValue<string>());
        Assert.StartsWith("1C", ((string?)s["assemblies"]![0])!);
    }
}
