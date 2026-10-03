using HsSteel.Assets;

namespace HsSteel.Tests;

public sealed class SectionCatalogHandoffTests
{
    private static SectionParseReport ValidReport()
    {
        const string text =
            "SPEC SHAPE M2 M3 M4 M5 M6 M7 WEIGHT PAINT COLOR FAMILY\n" +
            "H100x100x6x8 H 100 100 6 8 10 0 17.2 0.75 3 H-BEAM\n" +
            "H200x100x5.5x8 H 200 100 5.5 8 11 0 21.3 0.92 3 H-BEAM\n";
        return SectionTable.ParseDetailed(
            text,
            "H-BEAM",
            sourcePath: @"C:\HS-STEEL\HSSTEEL\attributes\H-BEAM.dat",
            sourceSha256: new string('a', 64),
            encodingName: "cp949");
    }

    [Fact]
    public void Handoff_is_source_hashed_bounded_and_non_authorizing()
    {
        var handoff = SectionCatalogHandoff.Build(ValidReport(), "H100", 50);

        Assert.Equal("hs-steel-section-catalog/1", handoff["schema"]!.GetValue<string>());
        Assert.Equal("khs0927/hs-steel-cad", handoff["producer"]!.GetValue<string>());
        Assert.Equal("PASS", handoff["validation_status"]!.GetValue<string>());
        Assert.Equal("single_family_file", handoff["capability_scope"]!.GetValue<string>());
        Assert.False(handoff["global_legacy_catalog_verified"]!.GetValue<bool>());
        Assert.False(handoff["execution_authorized"]!.GetValue<bool>());
        Assert.False(handoff["may_execute_mutation"]!.GetValue<bool>());
        Assert.Equal(1, handoff["returned_rows"]!.GetValue<int>());
        Assert.Equal("H100x100x6x8", handoff["rows"]![0]!["spec"]!.GetValue<string>());
        Assert.Equal(64, handoff["contract_digest"]!.GetValue<string>().Length);
    }

    [Fact]
    public void Handoff_requires_source_file_hash()
    {
        var report = SectionTable.ParseDetailed(
            "SPEC SHAPE M2 M3 M4 M5 M6 M7 WEIGHT PAINT COLOR FAMILY\n"
            + "H100x100x6x8 H 100 100 6 8 10 0 17.2 0.75 3 H-BEAM\n",
            "H-BEAM");

        Assert.Throws<InvalidOperationException>(() => SectionCatalogHandoff.Build(report));
    }

    [Fact]
    public void Handoff_refuses_quarantined_rows()
    {
        var report = SectionTable.ParseDetailed(
            "SPEC SHAPE M2 M3 M4 M5 M6 M7 WEIGHT PAINT COLOR FAMILY\n"
            + "H100x100x6x8 H 100 BAD 6 8 10 0 17.2 0.75 3 H-BEAM\n",
            "H-BEAM",
            sourcePath: "H-BEAM.dat",
            sourceSha256: new string('b', 64),
            encodingName: "cp949");

        Assert.Throws<FormatException>(() => SectionCatalogHandoff.Build(report));
    }

    [Fact]
    public void Handoff_digest_is_deterministic()
    {
        var first = SectionCatalogHandoff.Build(ValidReport());
        var second = SectionCatalogHandoff.Build(ValidReport());

        Assert.Equal(
            first["contract_digest"]!.GetValue<string>(),
            second["contract_digest"]!.GetValue<string>());
    }
}
