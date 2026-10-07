using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HsSteel.Knowledge.Ingest;

/// <summary>One manifest row's coverage status. Status is "covered", "deferred" (with an explicit Reason) or "uncovered".</summary>
public sealed record CoverageRow(string RelPath, string Target, string Status, IReadOnlyList<string> Tables, string? Reason);

/// <summary>
/// Builds out/knowledge/coverage.json: for every manifest row with disposition ingest, which DB table(s) hold it. A row is covered when
/// some table's source_file_id (or an edge's evidence_source_file) points at it; .xtp palette copies are covered through the .atc of the same
/// palette id; files that failed to parse are deferred with the parser's message.
/// </summary>
public static class Coverage
{
    public const string SchemaId = "hs-coverage/1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyList<CoverageRow> Compute(SqliteConnection cn, Manifest manifest, string? root = null)
    {
        var o = StringComparer.Ordinal;
        var tables = new List<string>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'search_fts%' ORDER BY name";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                tables.Add(r.GetString(0));
            }
        }

        var fileIdByRel = new Dictionary<string, long>(o);
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT id, rel_path FROM source_file";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                fileIdByRel[r.GetString(1)] = r.GetInt64(0);
            }
        }

        var coveredBy = new Dictionary<long, SortedSet<string>>();
        var failedMsg = new Dictionary<long, string>();
        void Mark(long id, string table)
        {
            if (!coveredBy.TryGetValue(id, out var s))
            {
                coveredBy[id] = s = new SortedSet<string>(o);
            }

            s.Add(table);
        }

        foreach (var t in tables.Where(t => t is not ("source_file" or "ingest_failure")))
        {
            using var info = cn.CreateCommand();
            info.CommandText = $"PRAGMA table_info('{t}')";
            var hasCol = false;
            using (var r = info.ExecuteReader())
            {
                while (r.Read())
                {
                    hasCol |= r.GetString(1) == "source_file_id";
                }
            }

            if (!hasCol)
            {
                continue;
            }

            using var q = cn.CreateCommand();
            q.CommandText = $"SELECT DISTINCT source_file_id FROM \"{t}\" WHERE source_file_id IS NOT NULL";
            using var rr = q.ExecuteReader();
            while (rr.Read())
            {
                Mark(rr.GetInt64(0), t);
            }
        }

        if (tables.Contains("edge"))
        {
            using var q = cn.CreateCommand();
            q.CommandText = "SELECT DISTINCT evidence_source_file FROM edge WHERE evidence_source_file IS NOT NULL";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                var rel = r.GetString(0).Split('!')[0].Replace('\\', '/');
                if (fileIdByRel.TryGetValue(rel, out var id))
                {
                    Mark(id, "edge");
                }
            }
        }

        if (tables.Contains("ingest_failure"))
        {
            using var q = cn.CreateCommand();
            q.CommandText = "SELECT source_file_id, group_concat(stage || ': ' || message, ' | ') FROM (SELECT * FROM ingest_failure ORDER BY id) WHERE source_file_id IS NOT NULL GROUP BY source_file_id";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                failedMsg[r.GetInt64(0)] = r.GetString(1);
            }
        }

        // palette ids of ingested .atc files (guid is part of the file name) for .xtp duplicate resolution
        var atcByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in manifest.Files.Where(f => f.Disposition == ManifestScanner.Ingest && f.RelPath.EndsWith(".atc", StringComparison.OrdinalIgnoreCase)))
        {
            var stem = Path.GetFileNameWithoutExtension(f.RelPath);
            var us = stem.LastIndexOf('_');
            if (us > 0 && fileIdByRel.TryGetValue(f.RelPath, out var id) && coveredBy.ContainsKey(id))
            {
                atcByGuid[stem[(us + 1)..].Trim('{', '}')] = f.RelPath;
            }
        }

        var rows = new List<CoverageRow>();
        foreach (var f in manifest.Files.Where(f => f.Disposition == ManifestScanner.Ingest).OrderBy(f => f.RelPath, o))
        {
            fileIdByRel.TryGetValue(f.RelPath, out var id);
            if (coveredBy.TryGetValue(id, out var t) && t.Count > 0)
            {
                rows.Add(new CoverageRow(f.RelPath, f.Target, "covered", t.ToList(), null));
                continue;
            }

            if (f.RelPath.EndsWith(".xtp", StringComparison.OrdinalIgnoreCase) && root is not null)
            {
                var path = Path.Combine(root, f.RelPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                {
                    var head = File.ReadAllText(path, Encoding.UTF8);
                    var m = System.Text.RegularExpressions.Regex.Match(head, "<ItemID idValue=\"\\{([0-9A-Fa-f-]{36})\\}\"");
                    if (m.Success && atcByGuid.TryGetValue(m.Groups[1].Value, out var atc))
                    {
                        rows.Add(new CoverageRow(f.RelPath, f.Target, "covered", ["palette_item"], "duplicate export of palette " + atc + " (same palette id)"));
                        continue;
                    }
                }
            }

            if (failedMsg.TryGetValue(id, out var msg))
            {
                rows.Add(new CoverageRow(f.RelPath, f.Target, "deferred", ["ingest_failure"], "parse failure: " + msg));
                continue;
            }

            rows.Add(new CoverageRow(f.RelPath, f.Target, "uncovered", [], null));
        }

        return rows;
    }

    internal static IngestSummary Write(SqliteConnection cn, Manifest manifest, string coveragePath, string root, IReadOnlyDictionary<string, int> counts, IReadOnlyList<string> failures)
    {
        var rows = Compute(cn, manifest, root);
        var covered = rows.Count(r => r.Status == "covered");
        var deferred = rows.Count(r => r.Status == "deferred");
        var uncovered = rows.Count(r => r.Status == "uncovered");
        var byTable = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows.Where(r => r.Status == "covered"))
        {
            foreach (var t in r.Tables)
            {
                byTable[t] = byTable.GetValueOrDefault(t) + 1;
            }
        }

        var doc = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schema"] = SchemaId,
            ["ingestRows"] = rows.Count,
            ["covered"] = covered,
            ["deferred"] = deferred,
            ["uncovered"] = uncovered,
            ["coveredPercent"] = rows.Count == 0 ? 100.0 : Math.Round(100.0 * covered / rows.Count, 2),
            ["resolvedPercent"] = rows.Count == 0 ? 100.0 : Math.Round(100.0 * (covered + deferred) / rows.Count, 2),
            ["coveredByTable"] = byTable,
            ["failures"] = failures,
            ["rows"] = rows.Select(r => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["relPath"] = r.RelPath, ["target"] = r.Target, ["status"] = r.Status, ["tables"] = r.Tables, ["reason"] = r.Reason,
            }).ToList(),
        };
        var full = Path.GetFullPath(coveragePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, JsonSerializer.Serialize(doc, JsonOpts).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
        _ = CultureInfo.InvariantCulture;
        return new IngestSummary(counts, failures, rows.Count, covered, deferred, uncovered, full);
    }
}
