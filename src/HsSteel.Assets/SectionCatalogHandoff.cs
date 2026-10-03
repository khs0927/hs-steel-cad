using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HsSteel.Assets;

/// <summary>Builds a bounded, source-hashed section-table handoff for external consumers.</summary>
public static class SectionCatalogHandoff
{
    private static string CanonicalJson(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject obj => "{" + string.Join(",", obj
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{JsonSerializer.Serialize(pair.Key)}:{CanonicalJson(pair.Value)}")) + "}",
        JsonArray arr => "[" + string.Join(",", arr.Select(CanonicalJson)) + "]",
        _ => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
    };

    private static string Digest(JsonNode node) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson(node))))
            .ToLowerInvariant();

    public static JsonObject Build(
        SectionParseReport report,
        string? query = null,
        int limit = 200)
    {
        if (!report.IsValid)
        {
            throw new FormatException(
                $"Section family {report.Family} has {report.QuarantinedRows} quarantined row(s).");
        }

        if (string.IsNullOrWhiteSpace(report.SourceSha256)
            || report.SourceSha256.Length != 64
            || report.SourceSha256.Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new InvalidOperationException(
                "A source-file SHA-256 is required before section data can cross repositories.");
        }

        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be 1-500.");
        }

        var matched = report.Rows
            .Where(row => string.IsNullOrWhiteSpace(query)
                || row.Spec.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToArray();

        var rows = new JsonArray();
        foreach (var row in matched)
        {
            rows.Add(new JsonObject
            {
                ["spec"] = row.Spec,
                ["shape"] = row.Shape,
                ["dimensions_mm"] = new JsonArray(row.M.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["unit_weight_kg_m"] = row.UnitWeight,
                ["paint_area_m2_m"] = row.PaintArea,
                ["aci_color"] = row.Color,
                ["family"] = row.Family,
            });
        }

        var payload = new JsonObject
        {
            ["schema"] = "hs-steel-section-catalog/1",
            ["producer"] = "khs0927/hs-steel-cad",
            ["family"] = report.Family,
            ["source_file"] = report.SourcePath is null ? null : Path.GetFileName(report.SourcePath),
            ["source_sha256"] = report.SourceSha256.ToLowerInvariant(),
            ["encoding"] = report.EncodingName,
            ["validation_status"] = "PASS",
            ["capability_scope"] = "single_family_file",
            ["global_legacy_catalog_verified"] = false,
            ["read_rows"] = report.ReadRows,
            ["accepted_rows"] = report.AcceptedRows,
            ["quarantined_rows"] = report.QuarantinedRows,
            ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
            ["returned_rows"] = rows.Count,
            ["rows"] = rows,
            ["execution_authorized"] = false,
            ["may_execute_mutation"] = false,
        };
        payload["contract_digest"] = Digest(payload);
        return payload;
    }
}
