using HsSteel.Assets;
using HsSteel.Domain;
using HsSteel.Mcp;
using HsSteel.Modeling;

namespace HsSteel.Tests;

/// <summary>
/// docs/DECISIONS_BOLT_MARKS.md: bolt add length by bolt type (TS 1 washer, HTB KCS), assembly marks zero-padded to the
/// Numbering.dat width (C001), embed assemblies headed EB (legacy M80 EMBED head), all overridable per project.
/// </summary>
public sealed class BoltMarkDecisionTests
{
    private static ModelResult Build(Project p) => new ModelBuilder(SectionCatalog.Empty, SpliceStandards.Empty).Build(p);

    // ---- decision 1: bolt add length ----

    [Theory]
    [InlineData(16, 25, 30)]
    [InlineData(20, 30, 35)]
    [InlineData(22, 35, 40)]
    [InlineData(24, 40, 45)]
    [InlineData(27, 45, 50)]
    [InlineData(30, 50, 55)]
    public void Ts_add_length_is_the_hex_value_minus_one_washer_step(double dia, double ts, double hex)
    {
        Assert.Equal(ts, StandardOptions.AddLength(null, Bolts.S10T, dia));
        Assert.Equal(hex, StandardOptions.AddLength(null, Bolts.F10T, dia));
        Assert.Equal(5, hex - ts);
    }

    [Theory]
    [InlineData("TS M20", 30)]
    [InlineData("HTB M20", 35)]
    [InlineData("F10T M20", 35)]
    [InlineData("M20", 35)] // unnamed grade = F10T hex set
    public void Default_picks_the_table_from_the_bolt_name(string name, double add) =>
        Assert.Equal(add, StandardOptions.AddLength(StandardOptions.DefaultBoltLengthTable, Bolts.GradeOf(name), 20));

    [Theory]
    [InlineData(16, 17, 45)] // legacy SCSS .dat "TS M16*45": grip 17 + 25 = 42 -> 45
    [InlineData(20, 26, 60)] // "TS M20*60": 26 + 30 = 56 -> 60
    [InlineData(22, 40, 75)] // 40 + 35 = 75 exactly
    public void Ts_lengths_reproduce_the_legacy_scss_rows(double dia, double grip, double length) =>
        Assert.Equal(length, StandardOptions.BoltLength(null, Bolts.S10T, dia, grip));

    [Fact]
    public void Project_can_force_one_table_for_every_bolt()
    {
        Assert.Equal(35, StandardOptions.AddLength("kcs", Bolts.S10T, 20));
        Assert.Equal(30, StandardOptions.AddLength("ts_one_washer", Bolts.F10T, 20));
    }

    // ---- decision 2: mark number width ----

    [Theory]
    [InlineData("C001", "G001", 3)]
    [InlineData("C01", "G01", 2)]   // M80-era two-digit heads
    [InlineData("C1", "G1", 1)]
    [InlineData("C", "G", null)]   // no digits -> no opinion
    public void Digits_come_from_the_numbering_templates(string column, string girder, int? digits)
    {
        var dat = new Dictionary<string, string> { ["M83-COLUMN-HD-BOX"] = column, ["M83-GIRDER-HD-BOX"] = girder };
        Assert.Equal(digits, AssemblyTypes.DigitsFrom(dat));
        Assert.Equal(digits ?? StandardOptions.DefaultMarkDigits, DetailRules.From(dat).MarkDigits);
    }

    [Fact]
    public void Digits_use_the_most_common_template_width()
    {
        var dat = new Dictionary<string, string>
        {
            ["M83-COLUMN-HD-BOX"] = "C001", ["M83-GIRDER-HD-BOX"] = "G001", ["M83-BEAM---HD-BOX"] = "B01",
        };
        Assert.Equal(3, AssemblyTypes.DigitsFrom(dat));
        Assert.Null(AssemblyTypes.DigitsFrom(new Dictionary<string, string>()));
    }

    [Fact]
    public void Workspace_reads_mark_width_and_heads_from_numbering_dat()
    {
        var root = Path.Combine(Path.GetTempPath(), "hs-num-" + Guid.NewGuid().ToString("N"));
        var attr = Directory.CreateDirectory(Path.Combine(root, "attributes")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(attr, "Numbering.dat"),
                "000000000-000000000000\n(\"M83-COLUMN-HD-BOX\" \"MC01\")\n(\"M83-GIRDER-HD-BOX\" \"G01\")\n(\"M83-EMBED--HD-BOX\" \"AB01\")\n"); // legacy layout
            var ws = Workspace.Create(root, Path.Combine(root, "projects"));
            Assert.Equal(2, ws.Rules.MarkDigits);
            Assert.Equal("MC", ws.Rules.MarkHead(AssemblyType.Column));
            Assert.Equal("AB", ws.Rules.MarkHead(AssemblyType.Embed));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Zero_padding_keeps_ordinal_order_past_nine()
    {
        var marks = Enumerable.Range(1, 12).Select(n => StandardOptions.FormatMark("C", n, 3)).ToList();
        Assert.Equal(marks, marks.Order(StringComparer.Ordinal));
    }

    // ---- decision 3: embed head ----

    private static Project WithEmbed(DetailRules? rules) => new()
    {
        Name = "EMB",
        Rules = rules,
        Members =
        [
            new MemberDef("C1", AssemblyType.Column, "H300x300x10x15", new V3(0, 0, 0), new V3(0, 0, 4000)),
            new MemberDef("E1", AssemblyType.Embed, "L75x75x6", new V3(0, 3000, 0), new V3(1000, 3000, 0)),
        ],
    };

    [Fact]
    public void Embed_assemblies_are_marked_EB_by_default()
    {
        Assert.Equal("EB", AssemblyTypes.Prefix(AssemblyType.Embed));
        Assert.Equal("EB001", Build(WithEmbed(null)).MemberMarks["E1"]);
        Assert.Equal("C001", Build(WithEmbed(null)).MemberMarks["C1"]);
    }

    [Fact]
    public void Embed_head_is_overridable()
    {
        var alt = Build(WithEmbed(new DetailRules { MarkScheme = "alt" }));
        Assert.Equal("AB001", alt.MemberMarks["E1"]);
        var custom = Build(WithEmbed(new DetailRules { MarkHeads = new Dictionary<AssemblyType, string> { [AssemblyType.Embed] = "EM" }, MarkDigits = 1 }));
        Assert.Equal("EM1", custom.MemberMarks["E1"]);
    }

    [Fact]
    public void Embed_head_override_round_trips_through_project_json()
    {
        var p = WithEmbed(new DetailRules { MarkHeads = new Dictionary<AssemblyType, string> { [AssemblyType.Embed] = "EM" } });
        var json = p.ToJson();
        Assert.Contains("\"embed\": \"EM\"", json, StringComparison.Ordinal); // documented in DECISIONS_BOLT_MARKS.md
        Assert.Equal("EM001", Build(Project.FromJson(json)).MemberMarks["E1"]);
    }

    [Fact]
    public void Embed_head_falls_back_to_the_legacy_m80_key()
    {
        var m80 = new Dictionary<string, string> { [AssemblyTypes.LegacyEmbedKey] = "EB01" };
        Assert.Equal("EB", AssemblyTypes.HeadsFrom(m80)[AssemblyType.Embed]);
        var both = new Dictionary<string, string> { [AssemblyTypes.LegacyEmbedKey] = "EB01", ["M83-EMBED--HD-BOX"] = "EP001" };
        Assert.Equal("EP", AssemblyTypes.HeadsFrom(both)[AssemblyType.Embed]); // the M83 key wins
    }
}