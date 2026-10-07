using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace HsSteel.Knowledge;

public sealed record NeighborHit(GraphNode Node, GraphEdge Edge, int Depth);

/// <summary>Read-only query API over hs_assets.db.</summary>
public sealed class KnowledgeStore : IDisposable
{
    private readonly SqliteConnection cn;

    public KnowledgeStore(string dbPath)
    {
        cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(Path.GetFullPath(dbPath), SqliteOpenMode.ReadOnly));
        cn.Open();
    }

    public void Dispose() => cn.Dispose();

    /// <summary>Exact normalized spec/alias matches first (by alias priority, then lighter section), then FTS5 bm25.</summary>
    public IReadOnlyList<SearchHit> Search(string query, string? kind = null, int limit = 20)
    {
        var hits = new List<SearchHit>();
        var seen = new HashSet<(string, string)>();
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return hits;
        }

        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT a.target_kind, a.target_key, COALESCE(n.label, a.target_key), MIN(a.priority) AS p
                FROM section_alias a
                LEFT JOIN section s ON s.id = a.section_id
                LEFT JOIN node n ON n.kind = a.target_kind AND n.key = a.target_key
                WHERE a.alias_norm = $q AND ($k IS NULL OR a.target_kind = $k)
                GROUP BY a.target_kind, a.target_key
                ORDER BY p, COALESCE(s.unit_weight, 0), a.target_key
                LIMIT $n
                """;
            cmd.Parameters.AddWithValue("$q", SpecAliases.Normalize(query));
            cmd.Parameters.AddWithValue("$k", (object?)kind ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$n", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (seen.Add((r.GetString(0), r.GetString(1))))
                {
                    hits.Add(new SearchHit(r.GetString(0), r.GetString(1), r.GetString(2), 1000 - r.GetInt64(3), "exact"));
                }
            }
        }

        foreach (var op in new[] { " ", " OR " })
        {
            if (hits.Count >= limit)
            {
                break;
            }

            var match = FtsExpression(query, op);
            if (match.Length == 0)
            {
                break;
            }

            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                SELECT kind, key, label, bm25(search_fts, 0, 0, 10.0, 1.0) AS rank
                FROM search_fts WHERE search_fts MATCH $m AND ($k IS NULL OR kind = $k)
                ORDER BY rank, kind, key LIMIT $n
                """;
            cmd.Parameters.AddWithValue("$m", match);
            cmd.Parameters.AddWithValue("$k", (object?)kind ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$n", limit * 4);
            using var r = cmd.ExecuteReader();
            while (r.Read() && hits.Count < limit)
            {
                if (seen.Add((r.GetString(0), r.GetString(1))))
                {
                    hits.Add(new SearchHit(r.GetString(0), r.GetString(1), r.GetString(2), -r.GetDouble(3), op == " " ? "fts" : "fts-any"));
                }
            }
        }

        return hits.Take(limit).ToList();
    }

    /// <summary>Quoted prefix terms; spec-like tokens are also tried in normalized form (e.g. "H-400" -> "H400").</summary>
    internal static string FtsExpression(string query, string op)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(ch => char.IsLetterOrDigit(ch) || ch == '.').ToArray()))
            .Where(t => t.Length > 0)
            .Select(t => "\"" + t + "\"*")
            .ToList();
        return string.Join(op, terms);
    }

    public GraphNode? Get(string kind, string key)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id, kind, key, label, props_json FROM node WHERE kind = $k AND key = $key";
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$key", key);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadNode(r) : null;
    }

    public GraphNode? GetNode(long id)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id, kind, key, label, props_json FROM node WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadNode(r) : null;
    }

    /// <summary>Breadth-first neighbors following edges in both directions, up to <paramref name="depth"/> hops.</summary>
    public IReadOnlyList<NeighborHit> Neighbors(long nodeId, string? rel = null, int depth = 1)
    {
        var result = new List<NeighborHit>();
        var visited = new HashSet<long> { nodeId };
        var frontier = new List<long> { nodeId };
        for (var d = 1; d <= depth && frontier.Count > 0; d++)
        {
            var next = new List<long>();
            foreach (var id in frontier)
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandText = """
                    SELECT src, rel, dst, evidence_source_file, evidence_note FROM edge
                    WHERE (src = $id OR dst = $id) AND ($rel IS NULL OR rel = $rel)
                    ORDER BY src, rel, dst
                    """;
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$rel", (object?)rel ?? DBNull.Value);
                var edges = new List<GraphEdge>();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        edges.Add(new GraphEdge(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
                    }
                }

                foreach (var e in edges)
                {
                    var other = e.Src == id ? e.Dst : e.Src;
                    if (visited.Add(other) && GetNode(other) is { } n)
                    {
                        result.Add(new NeighborHit(n, e, d));
                        next.Add(other);
                    }
                }
            }

            frontier = next;
        }

        return result;
    }

    /// <summary>SHA-256 per table over all rows in primary/rowid order (determinism check).</summary>
    public static IReadOnlyDictionary<string, string> TableHashes(string dbPath)
    {
        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(Path.GetFullPath(dbPath), SqliteOpenMode.ReadOnly));
        cn.Open();
        var tables = new List<string>();
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table') AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'search_fts_%' ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                tables.Add(r.GetString(0));
            }
        }

        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in tables)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{t}\" ORDER BY rowid";
            if (t == "meta" || t == "doc_chunk")
            {
                cmd.CommandText = $"SELECT * FROM \"{t}\" ORDER BY 1";
            }

            using var r = cmd.ExecuteReader();
            using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (r.Read())
            {
                for (var i = 0; i < r.FieldCount; i++)
                {
                    var v = r.GetValue(i);
                    var bytes = v is byte[] b ? b : Encoding.UTF8.GetBytes(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "\0");
                    h.AppendData(bytes);
                    h.AppendData([0x1f]);
                }

                h.AppendData([0x1e]);
            }

            result[t] = Convert.ToHexStringLower(h.GetHashAndReset());
        }

        return result;
    }

    private static GraphNode ReadNode(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4));
}
