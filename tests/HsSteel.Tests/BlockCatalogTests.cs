using HsSteel.Assets.Blocks;

namespace HsSteel.Tests;

public class BlockCatalogTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const string Dir = @"C:\HS-STEEL\HSSTEEL\block";

    [Fact]
    public void LoadsLegacyBlocks_Deterministically()
    {
        if (!Directory.Exists(Dir)) return;
        var total = Directory.GetFiles(Dir, "*.dwg").Length;
        var r1 = BlockCatalog.LoadWithFailures(Dir);
        var r2 = BlockCatalog.LoadWithFailures(Dir);
        output.WriteLine($"loaded={r1.Assets.Count} failures={r1.Failures.Count} attrBlocks={r1.Assets.Count(a => a.Attributes.Count > 0)} zeroExtents={r1.Assets.Count(a => a.Extents[2] <= a.Extents[0])}");
        foreach (var x in r1.Failures) output.WriteLine(x.Path + " :: " + x.Reason);
        Assert.Equal(total, r1.Assets.Count + r1.Failures.Count);
        Assert.True(r1.Assets.Count > 0, "no blocks loaded; failures: " + string.Join("; ", r1.Failures.Take(3)));
        Assert.Equal(r1.Assets.Select(a => a.Name), r2.Assets.Select(a => a.Name));
        Assert.Equal(r1.Assets.Select(a => a.Sha256), r2.Assets.Select(a => a.Sha256));
        Assert.Equal(r1.Assets.Select(a => a.EntityCount), r2.Assets.Select(a => a.EntityCount));
        Assert.Equal(r1.Assets.Select(a => string.Join(",", a.Extents)), r2.Assets.Select(a => string.Join(",", a.Extents)));
        Assert.Equal(r1.Assets.Select(a => a.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase), r1.Assets.Select(a => a.Name));
    }

    [Fact]
    public void SomeBlockHasAttributes_AndPreviewRenders()
    {
        if (!Directory.Exists(Dir)) return;
        var assets = BlockCatalog.Load(Dir);
        Assert.Contains(assets, a => a.Attributes.Count > 0);
        var withGeom = assets.First(a => a.Extents[2] > a.Extents[0]);
        var svg = BlockPreview.RenderSvg(withGeom, 64);
        Assert.StartsWith("<svg", svg);
    }
}
