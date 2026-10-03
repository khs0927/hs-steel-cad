using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using HsSteel.Assets;

namespace HsSteel.Tests;

public class SectionTableValidationTests
{
    private const string Header = "SPEC SHAPE M2 M3 M4 M5 M6 M7 UNIT_WEIGHT PAINT_AREA ACI_COLOR FAMILY\n";

    [Fact]
    public void Public_fixture_parses_without_proprietary_assets()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "H-BEAM.public.dat");
        var result = SectionTable.LoadValidated(path);

        Assert.Equal("PASS", result.Validation.Status);
        Assert.Equal("CP949", result.Validation.Encoding);
        Assert.Equal(2, result.Validation.ReadRows);
        Assert.Equal(2, result.Validation.AcceptedRows);
        Assert.Equal(0, result.Validation.QuarantinedRows);
        Assert.Empty(result.Validation.Diagnostics);
        Assert.Equal(64, result.Validation.SourceSha256.Length);
        Assert.Equal("H100x100x6x8", result.Rows[0].Spec);
        Assert.Equal(17.2, result.Rows[0].UnitWeight);
    }

    [Theory]
    [InlineData("BAD H 100 nope 6 8 10 0 17.2 0.7 7 H-BEAM", "INVALID_NUMBER")]
    [InlineData("BAD H 100 NaN 6 8 10 0 17.2 0.7 7 H-BEAM", "INVALID_NUMBER")]
    [InlineData("BAD H 100 Infinity 6 8 10 0 17.2 0.7 7 H-BEAM", "INVALID_NUMBER")]
    [InlineData("BAD H 100 -1 6 8 10 0 17.2 0.7 7 H-BEAM", "NEGATIVE_DIMENSION")]
    [InlineData("BAD H 100 100 6 8 10 0 nope 0.7 7 H-BEAM", "INVALID_UNIT_WEIGHT")]
    [InlineData("BAD H 100 100 6 8 10 0 17.2 nope 7 H-BEAM", "INVALID_PAINT_AREA")]
    [InlineData("BAD H 100 100 6 8 10 0 17.2 0.7 999 H-BEAM", "ACI_COLOR_OUT_OF_RANGE")]
    [InlineData("BAD H 100 100 6 8 10 0 17.2 0.7 7.5 H-BEAM", "INVALID_ACI_COLOR")]
    public void Invalid_values_are_quarantined_not_repaired_to_zero(string row, string code)
    {
        var result = SectionTable.ParseValidated(Header + row + "\n", "H-BEAM");

        Assert.Empty(result.Rows);
        Assert.Equal(1, result.ReadRows);
        Assert.Equal(0, result.AcceptedRows);
        Assert.Equal(1, result.QuarantinedRows);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
        Assert.Throws<SectionTableParseException>(() => SectionTable.Parse(Header + row + "\n", "H-BEAM"));
    }

    [Fact]
    public void Valid_zero_optional_dimension_is_preserved()
    {
        var result = SectionTable.ParseValidated(
            Header + "H100x100x6x8 H 100 100 6 8 10 0 17.2 0.798 7 H-BEAM\n",
            "H-BEAM");

        var row = Assert.Single(result.Rows);
        Assert.Equal(0, row.M[5]);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Short_rows_are_quarantined_with_line_and_raw_text()
    {
        var raw = "BROKEN H 100 100";
        var result = SectionTable.ParseValidated(Header + raw + "\n", "H-BEAM");

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(2, error.LineNumber);
        Assert.Equal("FIELD_COUNT", error.Code);
        Assert.Equal(raw, error.Raw);
        Assert.Equal(1, result.QuarantinedRows);
    }

    [Fact]
    public void Load_validated_records_file_hash_and_cp949_bytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hs-section-{Guid.NewGuid():N}.dat");
        try
        {
            var text = Header + "H200x100x5.5x8 H 200 100 5.5 8 11 0 21.3 0.91 5 H-BEAM\n";
            var bytes = SectionTable.Cp949.GetBytes(text);
            File.WriteAllBytes(path, bytes);

            var result = SectionTable.LoadValidated(path);
            var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            Assert.Equal(expectedHash, result.Validation.SourceSha256);
            Assert.Equal("CP949", result.Validation.Encoding);
            Assert.Equal(1, result.Validation.AcceptedRows);
            Assert.Equal(Path.GetFullPath(path), result.Validation.SourcePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_proprietary_assets_are_reported_as_not_run()
    {
        var missing = Path.Combine(Path.GetTempPath(), "missing-hs-assets-" + Guid.NewGuid().ToString("N"));
        var report = JsonSerializer.SerializeToNode(SectionCatalogValidation.ValidateDirectory(missing))!.AsObject();

        Assert.Equal("NOT_RUN", report["status"]!.GetValue<string>());
        Assert.Equal("hs-steel-section-catalog", report["capability"]!.GetValue<string>());
        Assert.Equal(0, report["files_read"]!.GetValue<int>());
        Assert.Equal(0, report["rows_accepted"]!.GetValue<int>());
    }

    [Fact]
    public void Real_asset_coverage_is_explicit_pass_fail_or_not_run()
    {
        var report = JsonSerializer.SerializeToNode(SectionCatalogValidation.ValidateDirectory(Fx.Attr))!.AsObject();
        var status = report["status"]!.GetValue<string>();

        Assert.Contains(status, new[] { "PASS", "FAIL", "NOT_RUN" });
        if (status == "PASS")
        {
            Assert.True(report["files_read"]!.GetValue<int>() > 0);
            Assert.Equal(0, report["rows_quarantined"]!.GetValue<int>());
        }
        else if (status == "NOT_RUN")
        {
            Assert.False(Fx.HasLegacy);
        }
    }
}
