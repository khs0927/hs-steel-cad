using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HsSteel.Assets;

/// <summary>One row of an HS-STEEL attributes/*.dat section table (all columns kept).</summary>
/// <param name="Spec">Designation, e.g. "H300x150x6.5x9".</param>
/// <param name="Shape">Shape code (H, L, C, ...).</param>
/// <param name="M">M2..M7 dimensions in mm, meaning depends on the family (H: h, b, tw, tf, r).</param>
/// <param name="UnitWeight">kg/m.</param>
/// <param name="PaintArea">m2/m.</param>
/// <param name="Color">ACI color used by HS-STEEL for this family.</param>
/// <param name="Family">Family name (file name), e.g. "H-BEAM".</param>
public sealed record SectionRecord(
    string Spec,
    string Shape,
    IReadOnlyList<double> M,
    double UnitWeight,
    double PaintArea,
    int Color,
    string Family);

/// <summary>Evidence for one quarantined field/row in a section table.</summary>
public sealed record SectionParseIssue(
    int LineNumber,
    string Column,
    string RawValue,
    string ErrorCode,
    string Message,
    string RawLine);

/// <summary>Strict parse result. Invalid rows are quarantined instead of silently coerced.</summary>
public sealed record SectionParseReport(
    string Family,
    string EncodingName,
    string? SourcePath,
    string? SourceSha256,
    int ReadRows,
    int AcceptedRows,
    int QuarantinedRows,
    IReadOnlyList<SectionRecord> Rows,
    IReadOnlyList<SectionParseIssue> Issues)
{
    public bool IsValid => QuarantinedRows == 0 && Issues.Count == 0;
}

/// <summary>Reads HS-STEEL section tables (CP949, whitespace separated, first line is a header).</summary>
public static class SectionTable
{
    static SectionTable() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding Cp949 => Encoding.GetEncoding(949);

    public static IReadOnlyList<SectionRecord> Load(string path)
    {
        var report = LoadDetailed(path);
        EnsureValid(report);
        return report.Rows;
    }

    public static SectionParseReport LoadDetailed(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = Cp949.GetString(bytes);
        var family = Path.GetFileNameWithoutExtension(path);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return ParseDetailed(text, family, path, sha256, "cp949");
    }

    public static IReadOnlyList<SectionRecord> Parse(string text, string family)
    {
        var report = ParseDetailed(text, family);
        EnsureValid(report);
        return report.Rows;
    }

    public static SectionParseReport ParseDetailed(
        string text,
        string family,
        string? sourcePath = null,
        string? sourceSha256 = null,
        string encodingName = "text")
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            throw new ArgumentException("family must be non-empty", nameof(family));
        }

        var rows = new List<SectionRecord>();
        var issues = new List<SectionParseIssue>();
        var readRows = 0;
        var quarantinedRows = 0;
        var lines = text.Replace("", "", StringComparison.Ordinal).Split('
');

        for (var index = 1; index < lines.Length; index++)
        {
            var rawLine = lines[index];
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            readRows++;
            var lineNumber = index + 1;
            var fields = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var rowIssues = new List<SectionParseIssue>();
            if (fields.Length < 11)
            {
                rowIssues.Add(new(
                    lineNumber,
                    "row",
                    rawLine,
                    "COLUMN_COUNT",
                    $"Expected at least 11 columns but found {fields.Length}.",
                    rawLine));
                issues.AddRange(rowIssues);
                quarantinedRows++;
                continue;
            }

            var spec = fields[0];
            var shape = fields[1];
            if (string.IsNullOrWhiteSpace(spec))
            {
                rowIssues.Add(Issue(lineNumber, "Spec", spec, "MISSING_SPEC", "Specification is empty.", rawLine));
            }

            if (string.IsNullOrWhiteSpace(shape))
            {
                rowIssues.Add(Issue(lineNumber, "Shape", shape, "MISSING_SHAPE", "Shape code is empty.", rawLine));
            }

            var dimensions = new double[6];
            for (var j = 0; j < dimensions.Length; j++)
            {
                var column = $"M{j + 2}";
                if (TryFinite(fields[j + 2], out var value, out var code))
                {
                    if (value < 0)
                    {
                        rowIssues.Add(Issue(
                            lineNumber,
                            column,
                            fields[j + 2],
                            "NEGATIVE_DIMENSION",
                            "Dimensions must be non-negative.",
                            rawLine));
                    }
                    else
                    {
                        dimensions[j] = value;
                    }
                }
                else
                {
                    rowIssues.Add(Issue(
                        lineNumber,
                        column,
                        fields[j + 2],
                        code,
                        "Dimension must be a finite invariant-culture number.",
                        rawLine));
                }
            }

            if (rowIssues.All(issue => !issue.Column.StartsWith("M", StringComparison.Ordinal)) &&
                dimensions.All(value => value == 0))
            {
                rowIssues.Add(Issue(
                    lineNumber,
                    "M2..M7",
                    string.Join(" ", fields.Skip(2).Take(6)),
                    "NO_POSITIVE_DIMENSION",
                    "At least one section dimension must be greater than zero.",
                    rawLine));
            }

            var unitWeight = ParseFiniteField(fields[8], lineNumber, "UnitWeight", rawLine, rowIssues);
            if (unitWeight is not null && unitWeight <= 0)
            {
                rowIssues.Add(Issue(
                    lineNumber,
                    "UnitWeight",
                    fields[8],
                    "NON_POSITIVE_WEIGHT",
                    "Unit weight must be greater than zero.",
                    rawLine));
            }

            var paintArea = ParseFiniteField(fields[9], lineNumber, "PaintArea", rawLine, rowIssues);
            if (paintArea is not null && paintArea < 0)
            {
                rowIssues.Add(Issue(
                    lineNumber,
                    "PaintArea",
                    fields[9],
                    "NEGATIVE_PAINT_AREA",
                    "Paint area must be non-negative.",
                    rawLine));
            }

            int? color = null;
            if (!int.TryParse(fields[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedColor))
            {
                rowIssues.Add(Issue(
                    lineNumber,
                    "Color",
                    fields[10],
                    "COLOR_FORMAT",
                    "ACI color must be an integer.",
                    rawLine));
            }
            else if (parsedColor is < 1 or > 255)
            {
                rowIssues.Add(Issue(
                    lineNumber,
                    "Color",
                    fields[10],
                    "COLOR_RANGE",
                    "ACI color must be between 1 and 255.",
                    rawLine));
            }
            else
            {
                color = parsedColor;
            }

            if (rowIssues.Count > 0)
            {
                issues.AddRange(rowIssues);
                quarantinedRows++;
                continue;
            }

            rows.Add(new SectionRecord(
                spec,
                shape,
                dimensions,
                unitWeight!.Value,
                paintArea!.Value,
                color!.Value,
                fields.Length > 11 ? fields[11] : family));
        }

        return new SectionParseReport(
            family,
            encodingName,
            sourcePath,
            sourceSha256,
            readRows,
            rows.Count,
            quarantinedRows,
            rows,
            issues);
    }

    private static SectionParseIssue Issue(
        int lineNumber,
        string column,
        string rawValue,
        string code,
        string message,
        string rawLine) =>
        new(lineNumber, column, rawValue, code, message, rawLine);

    private static double? ParseFiniteField(
        string raw,
        int lineNumber,
        string column,
        string rawLine,
        List<SectionParseIssue> issues)
    {
        if (TryFinite(raw, out var value, out var code))
        {
            return value;
        }

        issues.Add(Issue(
            lineNumber,
            column,
            raw,
            code,
            "Value must be a finite invariant-culture number.",
            rawLine));
        return null;
    }

    private static bool TryFinite(string raw, out double value, out string errorCode)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            errorCode = "NUMERIC_FORMAT";
            return false;
        }

        if (!double.IsFinite(value))
        {
            errorCode = "NON_FINITE";
            return false;
        }

        errorCode = "";
        return true;
    }

    private static void EnsureValid(SectionParseReport report)
    {
        if (report.IsValid)
        {
            return;
        }

        var first = report.Issues[0];
        throw new FormatException(
            $"Section table {report.Family} contains {report.QuarantinedRows} quarantined row(s); " +
            $"first issue: line {first.LineNumber} {first.Column} {first.ErrorCode}: {first.RawValue!r}");
    }
}

/// <summary>Reads Project.dat: first line is an id, then LISP pairs ("KEY" value).</summary>
public static class ProjectSettings
{
    public static IReadOnlyDictionary<string, string> Load(string path) => Parse(File.ReadAllText(path, SectionTable.Cp949));

    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('
'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("("", StringComparison.Ordinal) || !line.EndsWith(')'))
            {
                continue;
            }

            var close = line.IndexOf('"', 2);
            if (close < 0)
            {
                continue;
            }

            var key = line[2..close];
            var value = line[(close + 1)..^1].Trim().Trim('"');
            map[key] = value;
        }

        return map;
    }
}
