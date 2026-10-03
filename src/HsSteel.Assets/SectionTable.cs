using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HsSteel.Assets;

/// <summary>One row of an HS-STEEL attributes/*.dat section table (all columns kept).</summary>
/// <param name="Spec">Designation, e.g. "H300x150x6.5x9".</param>
/// <param name="Shape">Shape code (H, L, C, ...).</param>
/// <param name="M">M2..M7 dimensions in mm, meaning depends on the family (H: h, b, tw, tf, r).</param>
/// <param name="UnitWeight">kg/m.</param>
/// <param name="PaintArea">m²/m.</param>
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

/// <summary>One quarantined field/row from a section table.</summary>
public sealed record SectionParseDiagnostic(
    int LineNumber,
    string Column,
    string Raw,
    string Code,
    string Message);

/// <summary>Strict parse outcome. Invalid rows are quarantined, never repaired with invented zeros.</summary>
public sealed record SectionParseResult(
    IReadOnlyList<SectionRecord> Rows,
    IReadOnlyList<SectionParseDiagnostic> Diagnostics,
    int ReadRows,
    int AcceptedRows,
    int QuarantinedRows)
{
    public bool IsValid => Diagnostics.Count == 0 && ReadRows == AcceptedRows;
}

/// <summary>File-level provenance and coverage for one section table.</summary>
public sealed record SectionFileValidation(
    string SourcePath,
    string SourceSha256,
    string Encoding,
    string Family,
    int ReadRows,
    int AcceptedRows,
    int QuarantinedRows,
    IReadOnlyList<SectionParseDiagnostic> Diagnostics)
{
    public string Status => Diagnostics.Count == 0 ? "PASS" : "FAIL";
}

/// <summary>Rows plus file-level provenance.</summary>
public sealed record SectionFileResult(
    IReadOnlyList<SectionRecord> Rows,
    SectionFileValidation Validation);

public sealed class SectionTableParseException(SectionParseResult result)
    : FormatException(
        $"Section table contains {result.Diagnostics.Count} validation error(s) across "
        + $"{result.QuarantinedRows} quarantined row(s).")
{
    public SectionParseResult Result { get; } = result;
}

/// <summary>Reads HS-STEEL section tables (CP949, whitespace separated, first line is a header).</summary>
public static class SectionTable
{
    private static readonly string[] NumericColumns = ["M2", "M3", "M4", "M5", "M6", "M7"];

    static SectionTable() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding Cp949 => Encoding.GetEncoding(949);

    /// <summary>
    /// Strict compatibility API: returns rows only when every non-empty data row is valid.
    /// Use <see cref="LoadValidated"/> when diagnostics are needed.
    /// </summary>
    public static IReadOnlyList<SectionRecord> Load(string path)
    {
        var result = LoadValidated(path);
        if (result.Validation.Diagnostics.Count > 0)
        {
            var parsed = new SectionParseResult(
                result.Rows,
                result.Validation.Diagnostics,
                result.Validation.ReadRows,
                result.Validation.AcceptedRows,
                result.Validation.QuarantinedRows);
            throw new SectionTableParseException(parsed);
        }

        return result.Rows;
    }

    public static SectionFileResult LoadValidated(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var family = Path.GetFileNameWithoutExtension(path);
        var parsed = ParseValidated(Cp949.GetString(bytes), family);
        return new SectionFileResult(
            parsed.Rows,
            new SectionFileValidation(
                Path.GetFullPath(path),
                hash,
                "CP949",
                family,
                parsed.ReadRows,
                parsed.AcceptedRows,
                parsed.QuarantinedRows,
                parsed.Diagnostics));
    }

    /// <summary>
    /// Strict compatibility API. Any malformed numeric value, non-finite value,
    /// negative physical value, invalid field count or invalid ACI value fails the parse.
    /// </summary>
    public static IReadOnlyList<SectionRecord> Parse(string text, string family)
    {
        var result = ParseValidated(text, family);
        if (!result.IsValid)
        {
            throw new SectionTableParseException(result);
        }

        return result.Rows;
    }

    public static SectionParseResult ParseValidated(string text, string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            throw new ArgumentException("family is required.", nameof(family));
        }

        var rows = new List<SectionRecord>();
        var diagnostics = new List<SectionParseDiagnostic>();
        var quarantinedRows = 0;
        var readRows = 0;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        // The legacy format uses the first line as a header.
        for (var i = 1; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            readRows++;
            var lineNumber = i + 1;
            var fields = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var rowDiagnostics = new List<SectionParseDiagnostic>();
            if (fields.Length < 11)
            {
                rowDiagnostics.Add(new(
                    lineNumber,
                    "row",
                    rawLine,
                    "FIELD_COUNT",
                    $"Expected at least 11 whitespace-separated fields, got {fields.Length}."));
                diagnostics.AddRange(rowDiagnostics);
                quarantinedRows++;
                continue;
            }

            var dims = new double[6];
            for (var n = 0; n < dims.Length; n++)
            {
                if (!TryFinite(fields[n + 2], out dims[n]))
                {
                    rowDiagnostics.Add(NumberError(lineNumber, NumericColumns[n], fields[n + 2]));
                }
                else if (dims[n] < 0)
                {
                    rowDiagnostics.Add(new(
                        lineNumber,
                        NumericColumns[n],
                        fields[n + 2],
                        "NEGATIVE_DIMENSION",
                        "Section dimensions must be non-negative."));
                }
            }

            var unitWeight = ParsePhysical(
                fields[8],
                lineNumber,
                "UNIT_WEIGHT",
                "INVALID_UNIT_WEIGHT",
                rowDiagnostics);
            var paintArea = ParsePhysical(
                fields[9],
                lineNumber,
                "PAINT_AREA",
                "INVALID_PAINT_AREA",
                rowDiagnostics);

            int color = 0;
            if (!int.TryParse(fields[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out color))
            {
                rowDiagnostics.Add(new(
                    lineNumber,
                    "ACI_COLOR",
                    fields[10],
                    "INVALID_ACI_COLOR",
                    "ACI color must be an integer."));
            }
            else if (color is < 0 or > 256)
            {
                rowDiagnostics.Add(new(
                    lineNumber,
                    "ACI_COLOR",
                    fields[10],
                    "ACI_COLOR_OUT_OF_RANGE",
                    "ACI color must be between 0 and 256."));
            }

            var rowFamily = fields.Length > 11 ? fields[11].Trim() : family.Trim();
            if (string.IsNullOrWhiteSpace(fields[0]))
            {
                rowDiagnostics.Add(new(lineNumber, "SPEC", fields[0], "EMPTY_SPEC", "Section spec is required."));
            }

            if (string.IsNullOrWhiteSpace(fields[1]))
            {
                rowDiagnostics.Add(new(lineNumber, "SHAPE", fields[1], "EMPTY_SHAPE", "Shape code is required."));
            }

            if (string.IsNullOrWhiteSpace(rowFamily))
            {
                rowDiagnostics.Add(new(lineNumber, "FAMILY", rowFamily, "EMPTY_FAMILY", "Family is required."));
            }

            if (rowDiagnostics.Count > 0)
            {
                diagnostics.AddRange(rowDiagnostics);
                quarantinedRows++;
                continue;
            }

            rows.Add(new SectionRecord(
                fields[0],
                fields[1],
                dims,
                unitWeight,
                paintArea,
                color,
                rowFamily));
        }

        return new SectionParseResult(rows, diagnostics, readRows, rows.Count, quarantinedRows);
    }

    private static double ParsePhysical(
        string raw,
        int lineNumber,
        string column,
        string code,
        List<SectionParseDiagnostic> diagnostics)
    {
        if (!TryFinite(raw, out var value))
        {
            diagnostics.Add(new(
                lineNumber,
                column,
                raw,
                code,
                "Value must be a finite invariant-culture number."));
            return double.NaN;
        }

        if (value < 0)
        {
            diagnostics.Add(new(
                lineNumber,
                column,
                raw,
                code,
                "Physical values must be non-negative."));
        }

        return value;
    }

    private static SectionParseDiagnostic NumberError(int lineNumber, string column, string raw) =>
        new(
            lineNumber,
            column,
            raw,
            "INVALID_NUMBER",
            "Value must be a finite invariant-culture number.");

    private static bool TryFinite(string raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);
}

/// <summary>
/// Capability-level validation for the section catalogue only.
/// The scope is section *.dat files; Project.dat and SCSS connection tables are separate capabilities.
/// </summary>
public static class SectionCatalogValidation
{
    public static object ValidateDirectory(string attributesDir)
    {
        if (!Directory.Exists(attributesDir))
        {
            return new
            {
                status = "NOT_RUN",
                capability = "hs-steel-section-catalog",
                attributes_dir = Path.GetFullPath(attributesDir),
                reason = "HS-STEEL legacy attributes directory is unavailable.",
                files = Array.Empty<object>(),
                files_read = 0,
                rows_read = 0,
                rows_accepted = 0,
                rows_quarantined = 0,
            };
        }

        var files = Directory.GetFiles(attributesDir, "*.dat")
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !name.Equals("Project.dat", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("SCSS-", StringComparison.OrdinalIgnoreCase);
            })
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var reports = files.Select(SectionTable.LoadValidated).ToList();
        var diagnostics = reports.Sum(result => result.Validation.Diagnostics.Count);
        return new
        {
            status = files.Count > 0 && diagnostics == 0 ? "PASS" : "FAIL",
            capability = "hs-steel-section-catalog",
            attributes_dir = Path.GetFullPath(attributesDir),
            files = reports.Select(result => result.Validation).ToArray(),
            files_read = reports.Count,
            rows_read = reports.Sum(result => result.Validation.ReadRows),
            rows_accepted = reports.Sum(result => result.Validation.AcceptedRows),
            rows_quarantined = reports.Sum(result => result.Validation.QuarantinedRows),
        };
    }
}

/// <summary>Reads Project.dat: first line is an id, then LISP pairs ("KEY" value).</summary>
public static class ProjectSettings
{
    public static IReadOnlyDictionary<string, string> Load(string path) =>
        Parse(File.ReadAllText(path, SectionTable.Cp949));

    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("(\"", StringComparison.Ordinal) || !line.EndsWith(')'))
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
