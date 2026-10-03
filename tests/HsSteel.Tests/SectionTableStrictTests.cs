using HsSteel.Assets;

namespace HsSteel.Tests;

public class SectionTableStrictTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "H-BEAM-mini.dat");

    [Fact]
    public void Public_fixture_is_always_validated_with_hash_and_coverage()
    {
        var report = SectionTable.LoadDetailed(FixturePath);

        Assert.True(report.IsValid);
        Assert.Equal("cp949", report.EncodingName);
        Assert.Equal(2, report.ReadRows);
        Assert.Equal(2, report.AcceptedRows);
        Assert.Equal(0, report.QuarantinedRows);
        Assert.Empty(report.Issues);
        Assert.NotNull(report.SourceSha256);
        Assert.Equal(64, report.SourceSha256!.Length);

        var coverage = SectionCatalogValidator.ValidateDirectory(
            Path.GetDirectoryName(FixturePath)!,
            ["H-BEAM-mini.dat"]);
        Assert.Equal(SectionCatalogValidationStatus.PASS, coverage.Status);
        Assert.Equal(1.0, coverage.Coverage);
        Assert.Equal(2, coverage.AcceptedRows);
    }

    [Fact]
    public void Bad_numeric_value_is_quarantined_not_coerced_to_zero()
    {
        var text =
            "SPEC SHAPE M2 M3 M4 M5 M6 M7 W PAINT COLOR FAMILY\n" +
            "H-BAD H nope 100 6 8 10 0 17.2 0.45 3 H-BEAM\n";

        var report = SectionTable.ParseDetailed(text, "H-BEAM");

        Assert.False(report.IsValid);
        Assert.Empty(report.Rows);
        Assert.Equal(1, report.ReadRows);
        Assert.Equal(0, report.AcceptedRows);
        Assert.Equal(1, report.QuarantinedRows);
        var issue = Assert.Single(report.Issues);
        Assert.Equal(2, issue.LineNumber);
        Assert.Equal("M2", issue.Column);
        Assert.Equal("nope", issue.RawValue);
        Assert.Equal("NUMERIC_FORMAT", issue.ErrorCode);
        Assert.Contains("H-BAD", issue.RawLine);
        Assert.Throws<FormatException>(() => SectionTable.Parse(text, "H-BEAM"));
    }

    [Theory]
    [InlineData("NaN", "NON_FINITE")]
    [InlineData("Infinity", "NON_FINITE")]
    public void Non_finite_numbers_are_rejected(string raw, string code)
    {
        var text =
            "SPEC SHAPE M2 M3 M4 M5 M6 M7 W PAINT COLOR FAMILY\n" +
            $"H-BAD H 100 100 6 8 10 0 {raw} 0.45 3 H-BEAM\n";

        var report = SectionTable.ParseDetailed(text, "H-BEAM");

        Assert.Contains(report.Issues, issue => issue.Column == "UnitWeight" && issue.ErrorCode == code);
        Assert.Empty(report.Rows);
    }

    [Fact]
    public void Invalid_ranges_and_non_integer_aci_are_rejected()
    {
        var text =
            "SPEC SHAPE M2 M3 M4 M5 M6 M7 W PAINT COLOR FAMILY\n" +
            "NEG H -1 100 6 8 10 0 17.2 0.45 3 H-BEAM\n" +
            "WEIGHT H 100 100 6 8 10 0 0 0.45 3 H-BEAM\n" +
            "PAINT H 100 100 6 8 10 0 17.2 -1 3 H-BEAM\n" +
            "COLOR0 H 100 100 6 8 10 0 17.2 0.45 0 H-BEAM\n" +
            "COLOR256 H 100 100 6 8 10 0 17.2 0.45 256 H-BEAM\n" +
            "COLORDEC H 100 100 6 8 10 0 17.2 0.45 3.5 H-BEAM\n";

        var report = SectionTable.ParseDetailed(text, "H-BEAM");

        Assert.Equal(6, report.ReadRows);
        Assert.Equal(6, report.QuarantinedRows);
        Assert.Empty(report.Rows);
        Assert.Contains(report.Issues, issue => issue.ErrorCode == "NEGATIVE_DIMENSION");
        Assert.Contains(report.Issues, issue => issue.ErrorCode == "NON_POSITIVE_WEIGHT");
        Assert.Contains(report.Issues, issue => issue.ErrorCode == "NEGATIVE_PAINT_AREA");
        Assert.Contains(report.Issues, issue => issue.ErrorCode == "COLOR_RANGE");
        Assert.Contains(report.Issues, issue => issue.ErrorCode == "COLOR_FORMAT");
    }

    [Fact]
    public void Short_rows_are_reported_instead_of_silently_skipped()
    {
        var text = "HEADER\nH100 H 100 100\n";

        var report = SectionTable.ParseDetailed(text, "H-BEAM");

        Assert.Equal(1, report.QuarantinedRows);
        var issue = Assert.Single(report.Issues);
        Assert.Equal("COLUMN_COUNT", issue.ErrorCode);
        Assert.Equal(2, issue.LineNumber);
    }

    [Fact]
    public void Missing_private_assets_report_not_run()
    {
        var path = Path.Combine(Path.GetTempPath(), "hs-steel-missing-" + Guid.NewGuid().ToString("N"));
        var report = SectionCatalogValidator.ValidateDirectory(path, ["H-BEAM.dat"]);

        Assert.Equal(SectionCatalogValidationStatus.NOT_RUN, report.Status);
        Assert.Equal(new[] { "H-BEAM.dat" }, report.MissingFiles);
        Assert.Equal(0, report.ParsedFiles);
        Assert.Equal(0, report.AcceptedRows);
    }

    [Fact]
    public void Missing_required_family_fails_coverage()
    {
        var report = SectionCatalogValidator.ValidateDirectory(
            Path.GetDirectoryName(FixturePath)!,
            ["H-BEAM-mini.dat", "MISSING.dat"]);

        Assert.Equal(SectionCatalogValidationStatus.FAIL, report.Status);
        Assert.Equal(new[] { "MISSING.dat" }, report.MissingFiles);
        Assert.Equal(0.5, report.Coverage);
    }

    [Fact]
    public void Legacy_capability_reports_not_run_instead_of_fake_pass_when_assets_are_absent()
    {
        var required = new[] { "H-BEAM.dat", "SQ-PIPE.dat", "STEEL-PIPE.dat", "ANGLE.dat" };
        var report = SectionCatalogValidator.ValidateDirectory(Fx.Attr, required);

        if (Fx.HasLegacy)
        {
            Assert.NotEqual(SectionCatalogValidationStatus.NOT_RUN, report.Status);
            Assert.Equal(required.Length, report.RequiredFiles.Count);
        }
        else
        {
            Assert.Equal(SectionCatalogValidationStatus.NOT_RUN, report.Status);
            Assert.Equal(required.Length, report.MissingFiles.Count);
        }
    }
}
