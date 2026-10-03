namespace HsSteel.Assets;

public enum SectionCatalogValidationStatus
{
    PASS,
    FAIL,
    NOT_RUN,
}

public sealed record SectionCatalogFileResult(
    string FileName,
    string? SourceSha256,
    int ReadRows,
    int AcceptedRows,
    int QuarantinedRows,
    IReadOnlyList<SectionParseIssue> Issues);

public sealed record SectionCatalogValidationReport(
    SectionCatalogValidationStatus Status,
    string Directory,
    IReadOnlyList<string> RequiredFiles,
    IReadOnlyList<string> MissingFiles,
    int ParsedFiles,
    int ReadRows,
    int AcceptedRows,
    int QuarantinedRows,
    IReadOnlyList<SectionCatalogFileResult> Files,
    string Note)
{
    public double Coverage =>
        RequiredFiles.Count == 0 ? 0 : (double)(RequiredFiles.Count - MissingFiles.Count) / RequiredFiles.Count;
}

public static class SectionCatalogValidator
{
    public static SectionCatalogValidationReport ValidateDirectory(
        string directory,
        IEnumerable<string> requiredFiles)
    {
        var required = requiredFiles
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (required.Length == 0)
        {
            throw new ArgumentException("At least one capability-specific section table is required.", nameof(requiredFiles));
        }

        if (!System.IO.Directory.Exists(directory))
        {
            return new(
                SectionCatalogValidationStatus.NOT_RUN,
                directory,
                required,
                required,
                0,
                0,
                0,
                0,
                [],
                "Required HS-STEEL asset directory is unavailable; no legacy asset validation ran.");
        }

        var missing = new List<string>();
        var files = new List<SectionCatalogFileResult>();
        foreach (var name in required)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                missing.Add(name);
                continue;
            }

            var report = SectionTable.LoadDetailed(path);
            files.Add(new(
                name,
                report.SourceSha256,
                report.ReadRows,
                report.AcceptedRows,
                report.QuarantinedRows,
                report.AcceptedRows == 0
                    ? report.Issues.Append(new SectionParseIssue(0, "table", "", "EMPTY_TABLE",
                        "Required section table contains no accepted data rows.", "")).ToArray()
                    : report.Issues));
        }

        var quarantined = files.Sum(file => file.QuarantinedRows);
        var status = missing.Count == 0 && quarantined == 0 &&
            files.All(file => file.AcceptedRows > 0 && file.Issues.Count == 0)
            ? SectionCatalogValidationStatus.PASS
            : SectionCatalogValidationStatus.FAIL;
        return new(
            status,
            directory,
            required,
            missing,
            files.Count,
            files.Sum(file => file.ReadRows),
            files.Sum(file => file.AcceptedRows),
            quarantined,
            files,
            status == SectionCatalogValidationStatus.PASS
                ? "All required section tables contain accepted rows and parsed without issues."
                : "Missing assets, empty tables or parse issues prevent catalog promotion.");
    }
}
