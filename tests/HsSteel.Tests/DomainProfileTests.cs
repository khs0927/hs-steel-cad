using HsSteel.Assets;
using HsSteel.Domain;

namespace HsSteel.Tests;

/// <summary>Cross-section geometry of every steel family against the legacy tables (independent oracle: table kg/m).</summary>
public class DomainProfileTests
{
    /// <summary>Solid steel families: the outline polygon must reproduce the table weight (A = W / 7.85e-3).</summary>
    public static readonly string[] SteelFamilies =
        ["H-BEAM", "BH-BEAM", "LH-BEAM", "PEB-BEAM", "I-BEAM", "T-BAR", "ANGLE", "CHANNEL", "C-CHANNEL", "Z-BAR", "SQ-PIPE", "STEEL-PIPE", "ROUND-BAR", "FLAT-BAR"];

    /// <summary>
    /// Legacy rows whose tabulated weight contradicts their own dimensions (checked by hand, kept out of the 3 % rule).
    /// Each must stay more than 3 % off, so a corrected table makes this list fail loudly.
    /// </summary>
    public static readonly Dictionary<string, string> LegacyAnomalies = new()
    {
        ["L200x200x35"] = "table 128 kg/m; (200+200-35)x35 mm² + fillets gives ~102 kg/m",
        ["C50x30x10x2.3"] = "table 0.1 kg/m (row marked 주문불가)",
        ["F915x3.2"] = "expanded-metal weight (7.66 kg/m) in the flat-bar table",
        ["BH350x350x40x40"] = "table 315.4 kg/m; plates 2x350x40 + 270x40 give 304.6 kg/m",
        ["BH600x700x50x30"] = "table 558.5 kg/m; plates 2x700x30 + 540x50 give 541.7 kg/m",
        ["T217x299x10x14"] = "table 53.0 kg/m; 299x14 + 203x10 + fillets give 50.8 kg/m",
    };

    [Fact]
    public void Outline_area_matches_table_area_within_3_percent_for_every_family()
    {
        if (!Fx.HasLegacy)
        {
            return;
        }

        var bad = new List<string>();
        var anomalies = new List<string>();
        foreach (var family in SteelFamilies)
        {
            var rows = SectionTable.Load(Path.Combine(Fx.Attr, family + ".dat"));
            Assert.NotEmpty(rows);
            foreach (var r in rows)
            {
                var p = Profile.FromRecord(r);
                var table = r.UnitWeight / 7.85e-3;
                var ratio = Profile.Area(p) / table;
                if (LegacyAnomalies.ContainsKey(r.Spec))
                {
                    anomalies.Add(r.Spec);
                    Assert.True(Math.Abs(ratio - 1) > 0.03, $"{r.Spec} is listed as anomalous but matches ({ratio:0.000})");
                    continue;
                }

                if (Math.Abs(ratio - 1) > 0.03)
                {
                    bad.Add($"{family} {r.Spec}: outline {Profile.Area(p):0} mm² vs table {table:0} mm² ({ratio:0.000})");
                }
            }
        }

        Assert.True(bad.Count == 0, $"{bad.Count} rows off by more than 3 %:\n" + string.Join("\n", bad));
        Assert.Equal(LegacyAnomalies.Keys.Order(), anomalies.Distinct().Order());
    }

    [Fact]
    public void Rolled_sections_carry_fillets_and_cold_formed_sections_bends()
    {
        var h = new Profile("H400x200x8x13", "H-BEAM", ShapeKind.I, [400, 200, 8, 13, 16, 0], 66, 1.5);
        var sharp = new Profile("BH400x200x8x13", "BH-BEAM", ShapeKind.I, [400, 200, 8, 13, 8, 0], 66, 1.5);
        Assert.Equal(16, h.RootRadius);
        Assert.Equal(0, sharp.RootRadius);
        Assert.True(h.Section()[0].Points.Count > sharp.Section()[0].Points.Count);

        // Four root fillets add 4 (1 - π/4) r² to the sharp outline.
        Assert.InRange(Profile.Area(h) - Profile.Area(sharp), 4 * (1 - (Math.PI / 4)) * 16 * 16, 1.03 * 4 * (1 - (Math.PI / 4)) * 16 * 16);

        var tube = new Profile("ㅁ100x100x4.5", "SQ-PIPE", ShapeKind.Box, [100, 100, 4.5, 4.5, 4.5, 9], 13.1, 0.4);
        Assert.Equal(9, tube.CornerRadius);
        Assert.All(tube.Section()[0].Points, p => Assert.InRange(p.X, 0, 100));

        // Z: flanges on opposite sides of the web.
        var z = new Profile("Z210x60x19x2", "Z-BAR", ShapeKind.Z, [210, 60, 19, 2, 2, 4], 5.6, 0.7);
        Assert.Equal(118, z.Width);
    }

    [Fact]
    public void Every_table_row_resolves_to_a_drawable_profile()
    {
        if (!Fx.HasLegacy)
        {
            return;
        }

        var cat = SectionCatalog.Load(Fx.Attr);
        Assert.All(cat.Rows, r =>
        {
            var p = cat.Resolve(r.Spec);
            Assert.True(p.Depth > 0 && p.Width > 0, r.Spec);
            var loops = p.Section();
            Assert.NotEmpty(loops);
            Assert.True(Profile.Area(p) > 0, r.Spec);
        });
    }
}
