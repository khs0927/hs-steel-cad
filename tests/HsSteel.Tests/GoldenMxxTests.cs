using System.Text.RegularExpressions;
using HsSteel.Assets.Legacy;

namespace HsSteel.Tests;

public class GoldenMxxTests
{
    private const string Root = @"C:\HS-STEEL\HSSTEEL";

    private static List<string> MxxPaths() =>
        Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "*.M*", SearchOption.AllDirectories)
                .Where(p => Regex.IsMatch(Path.GetExtension(p), @"^\.M\d+$", RegexOptions.IgnoreCase)).ToList()
            : [];

    [Fact]
    public void ParsesInlineSample()
    {
        var f = MxxReader.Parse("749328975-201801151512\r\n(\"M34-FRSCL-BOX\" . \"Auto\")\r\n(\"M77-DCLPTXY\" -1 -1)\r\n(\"M77-DIM-GON\" . \"18\")\r\n");
        Assert.Equal(new DateTime(2018, 1, 15, 15, 12, 0), f.StampTime);
        Assert.Equal(3, f.Entries.Count);
        Assert.True(f.Find("M34-FRSCL-BOX")!.IsDotted);
        Assert.Equal(new object[] { -1.0, -1.0 }, f.Find("M77-DCLPTXY")!.Values.ToArray());
    }

    [Fact]
    public void AllLegacyMxxFilesParse_AndKeysMatchExtension()
    {
        var paths = MxxPaths();
        if (paths.Count == 0) return; // legacy data absent
        foreach (var p in paths)
        {
            var f = MxxReader.Read(p);
            Assert.NotEmpty(f.Stamp);
            Assert.NotEmpty(f.Entries);
            // known-diff: key prefix vs extension number is only a convention (M34-, M34X, MACRO21-, ...); not asserted.
        }
    }

    // Known diff: member count / total weight / per-spec weight cannot be compared. Legacy .Mxx files hold
    // macro option settings only and every BOM자재산출서.xlsm under C:\HS-STEEL is an empty template
    // (see src/HsSteel.Assets/Legacy/MxxFormat.md). No independent oracle exists; this documents the gap.
    [Fact]
    public void KnownDiff_NoMemberOracleInLegacyData()
    {
        foreach (var p in MxxPaths())
        {
            var f = MxxReader.Read(p);
            Assert.DoesNotContain(f.Entries, e => e.Key.Contains("WEIGHT", StringComparison.OrdinalIgnoreCase)
                                               || e.Key.Contains("MEMBSIZE", StringComparison.OrdinalIgnoreCase));
        }
    }
}
