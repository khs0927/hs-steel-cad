using HsSteel.Assets.Support;

namespace HsSteel.Tests;

public class SupportCatalogTests
{
    private static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("HSSTEEL_SUPPORT_DIR") ?? @"C:\HS-STEEL\HSSTEEL\support";
        return Directory.Exists(d) ? d : null;
    }

    [Fact]
    public void LoadsNonEmptyCollections()
    {
        var dir = Dir();
        if (dir == null) { return; }

        var a = SupportCatalog.Load(dir);
        Assert.NotEmpty(a.PaletteItems);
        Assert.NotEmpty(a.Commands);
        Assert.NotEmpty(a.Aliases);
        Assert.NotEmpty(a.Linetypes);
        Assert.NotEmpty(a.MlineStyles);
        Assert.NotEmpty(a.FontMaps);
        Assert.Contains(a.Commands, c => c.LispFunction != null);
        Assert.Contains(a.MlineStyles, m => m.Name == "STANDARD");
    }

    [Fact]
    public void PaletteBlocksResolveToNames()
    {
        var dir = Dir();
        if (dir == null) { return; }

        var a = SupportCatalog.Load(dir);
        var withSource = a.PaletteItems.Where(p => p.BlockSourceFile != null).ToList();
        Assert.NotEmpty(withSource);
        Assert.All(withSource, p => Assert.False(string.IsNullOrWhiteSpace(p.BlockName)));
    }

    [Fact]
    public void LoadIsDeterministic()
    {
        var dir = Dir();
        if (dir == null) { return; }

        var a = SupportCatalog.Load(dir);
        var b = SupportCatalog.Load(dir);
        Assert.Equal(a.PaletteItems, b.PaletteItems);
        Assert.Equal(a.Commands, b.Commands);
        Assert.Equal(a.Aliases, b.Aliases);
        Assert.Equal(a.Linetypes, b.Linetypes);
        Assert.Equal(a.MlineStyles, b.MlineStyles);
        Assert.Equal(a.FontMaps, b.FontMaps);
        Assert.Equal(a.Warnings, b.Warnings);
    }

    [Fact]
    public void MissingDirYieldsWarning()
    {
        var a = SupportCatalog.Load(Path.Combine(Path.GetTempPath(), "no-such-hs-support-dir"));
        Assert.Empty(a.Commands);
        Assert.Single(a.Warnings);
    }

    [Theory]
    [InlineData("^C^C(hs-foo 1)", "hs-foo")]
    [InlineData("^C^C_line", null)]
    [InlineData("$M=$(if,$(eq,$(getvar,users1),),^C^C_hjh,^C^C(sclfreebtn 60))", "sclfreebtn")]
    public void ExtractsLispFunction(string macro, string? expected) =>
        Assert.Equal(expected, SupportCatalog.ExtractLispFunction(macro));

    [Fact]
    public void PrintCounts()
    {
        var dir = Dir();
        if (dir == null) { return; }

        var a = SupportCatalog.Load(dir);
        Console.WriteLine($"COUNTS palette={a.PaletteItems.Count} cmds={a.Commands.Count} alias={a.Aliases.Count} lin={a.Linetypes.Count} mln={a.MlineStyles.Count} fmp={a.FontMaps.Count} lisp={a.Commands.Count(c => c.LispFunction != null)} blocks={a.PaletteItems.Count(p => p.BlockName != null)}");
        foreach (var w in a.Warnings) { Console.WriteLine("WARN " + w); }
    }
}
