using HsSteel.Domain;
using HsSteel.Modeling;

namespace HsSteel.Tests;

/// <summary>Shear-tab default bolt rows: fit the clear web D − 2(tf + r) with KDS 14 31 25 / AISC 360 J3.4M edges.</summary>
public class ShearTabLayoutTests
{
    private static Profile H(double d, double b, double tw, double tf, double r) =>
        new($"H{d}x{b}x{tw}x{tf}", "H", ShapeKind.I, [d, b, tw, tf, r], 0, 0);

    [Fact]
    public void Shallow_H150_gets_one_row_inside_the_clear_web_and_is_flagged()
    {
        // Old rule: max(1, floor((150-14-80)/70)) = 1 interval -> 2 rows, plate 150 mm > clear web 120 mm.
        var l = ShearTabLayout.Default(H(150, 75, 5, 7, 8), 20);
        Assert.Equal(120, l.ClearWeb);
        Assert.Equal(1, l.Count);
        Assert.Equal(80, l.Rows.Total);                 // 40 + 40, inside 120
        Assert.Equal([40.0], l.Rows.Holes.ToList());
        Assert.Contains(l.Flags, f => f.Contains("2-12 rows", StringComparison.Ordinal));
    }

    [Fact]
    public void Web_too_shallow_for_edge_distances_still_draws_one_centred_row_and_flags_it()
    {
        var l = ShearTabLayout.Default(H(100, 100, 6, 8, 10), 20); // clear 64 < 2 x 40
        Assert.Equal(64, l.ClearWeb);
        Assert.Equal(1, l.Count);
        Assert.Equal(64, l.Rows.Total, 6);
        Assert.Equal([32.0], l.Rows.Holes.ToList());
        Assert.Contains(l.Flags, f => f.Contains("minimum edge distance", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(200, 100, 5.5, 8, 11, 2, 150)]   // clear 162
    [InlineData(250, 125, 6, 9, 12, 2, 150)]     // clear 208
    [InlineData(400, 200, 8, 13, 16, 4, 290)]    // clear 342
    [InlineData(600, 200, 11, 17, 22, 7, 500)]   // clear 522
    public void Rows_fill_the_clear_web_at_70_mm_pitch(double d, double b, double tw, double tf, double r, int rows, double total)
    {
        var p = H(d, b, tw, tf, r);
        var l = ShearTabLayout.Default(p, 20);
        var expectedRows = (int)Math.Floor((ShearTabLayout.ClearWebDepth(p) - 80) / 70) + 1;
        Assert.Equal(expectedRows, l.Count);
        if (expectedRows == rows)
        {
            Assert.Equal(total, l.Rows.Total);
        }

        Assert.True(l.Rows.Total <= l.ClearWeb);
        Assert.Empty(l.Flags);
    }

    [Theory]
    [InlineData(16, 28)]
    [InlineData(20, 34)]
    [InlineData(22, 38)]
    [InlineData(24, 42)]
    [InlineData(27, 48)]
    [InlineData(30, 52)]
    public void Minimum_sheared_edge_distance_follows_KDS_14_31_25_table(double dia, double edge)
    {
        Assert.Equal(edge, ShearTabLayout.MinEdgeSheared(dia));
    }

    [Fact]
    public void Large_bolts_raise_edge_and_pitch()
    {
        var l = ShearTabLayout.Default(H(500, 200, 10, 16, 20), 27); // clear 428
        Assert.Equal(48, l.Edge);                        // KDS / AISC J3.4M M27 sheared edge
        Assert.Equal(85, l.Pitch);                       // 3d = 81 -> 85
        Assert.Equal(4, l.Count);                        // floor((428-96)/85)+1
        Assert.True(l.Rows.Total <= l.ClearWeb);
    }

    [Fact]
    public void Every_rolled_H_size_fits_its_clear_web()
    {
        // KS D 3502 sizes (D, B, tw, tf, r) from H100 to H900.
        double[][] sizes =
        [
            [100, 50, 5, 7, 8], [100, 100, 6, 8, 10], [125, 60, 6, 8, 9], [150, 75, 5, 7, 8], [150, 150, 7, 10, 11],
            [175, 90, 5, 8, 9], [200, 100, 5.5, 8, 11], [200, 200, 8, 12, 13], [250, 125, 6, 9, 12], [300, 150, 6.5, 9, 13],
            [350, 175, 7, 11, 13], [400, 200, 8, 13, 16], [450, 200, 9, 14, 18], [500, 200, 10, 16, 20], [600, 200, 11, 17, 22],
            [700, 300, 13, 24, 28], [800, 300, 14, 26, 28], [900, 300, 16, 28, 28],
        ];
        foreach (var s in sizes)
        {
            var p = H(s[0], s[1], s[2], s[3], s[4]);
            var l = ShearTabLayout.Default(p, 20);
            Assert.True(l.Rows.Total <= ShearTabLayout.ClearWebDepth(p) + 1e-9, p.Spec);
            Assert.All(l.Rows.Holes, y => Assert.InRange(y, 0, l.Rows.Total));
            Assert.Equal(l.Count < 2, l.Flags.Count > 0);
        }
    }

    [Fact]
    public void Standard_axis_taller_than_the_clear_web_is_flagged()
    {
        var p = H(150, 75, 5, 7, 8);
        Assert.Single(ShearTabLayout.Check(p, BoltAxis.Parse("40+1A70+40")));  // 150 > 120
        Assert.Empty(ShearTabLayout.Check(p, BoltAxis.Parse("40+0A70+40")));
    }
}

public class ShearTabModelTests
{
    [LegacyAssetFact]
    public void Shallow_beam_shear_tab_fits_the_clear_web_and_is_flagged_in_the_model()
    {
        var p = new Project { Name = "SHALLOW" };
        p.Members.Add(new MemberDef("C1", AssemblyType.Column, "H300x300x10x15", new V3(0, 0, 0), new V3(0, 0, 4000)));
        p.Members.Add(new MemberDef("B1", AssemblyType.Beam, "H150x75x5x7", new V3(0, 0, 3500), new V3(4000, 0, 3500)));
        p.Connections.Add(new ShearTabDef("ST1", "B1", MemberEnd.Start, "C1"));
        var r = Fx.Ws.Value.Build(p);
        var beam = Fx.Ws.Value.Catalog.Resolve("H150x75x5x7");
        var clear = ShearTabLayout.ClearWebDepth(beam);
        var tabs = r.Assemblies.SelectMany(a => a.Attachments).Where(x => x.Part.Role == "SHEAR-TAB").Select(x => x.Part).ToList();
        Assert.NotEmpty(tabs);
        // Column support: the plate is PlatePart.Rect(t, height, width) so SizeU is the vertical size along the web.
        Assert.All(tabs, t => Assert.True(t.SizeU <= clear + 1e-6, $"plate {t.SizeU} mm > clear web {clear} mm"));
        Assert.Contains(r.Warnings, w => w.StartsWith("ST1:", StringComparison.Ordinal) && w.Contains("clear web", StringComparison.Ordinal));
    }
}
