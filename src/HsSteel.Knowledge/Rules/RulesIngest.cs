using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HsSteel.Knowledge.Rules;

/// <summary>Result of <see cref="RulesIngest.Apply"/>.</summary>
public sealed record RulesIngestReport(int Rules, int Evidence, int Nodes, int Governs, int EvidencedBy, IReadOnlyDictionary<string, int> TrustCounts)
{
    public string Summary =>
        $"rules: rule={Rules} ({string.Join(", ", TrustCounts.Select(t => $"{t.Key}={t.Value}"))}) rule_evidence={Evidence} nodes+={Nodes} governs={Governs} evidenced_by={EvidencedBy}";
}

/// <summary>
/// Post-build step: materializes <see cref="RulesCatalog"/> into hs_assets.db — tables <c>rule</c>/<c>rule_evidence</c>, graph nodes
/// of kind <c>rule</c> (+ <c>source_file</c>, <c>project_default</c>, <c>workbook_sheet</c> anchor nodes), edges
/// <c>governs</c> / <c>evidenced_by</c> and FTS rows. Idempotent and deterministic: own rows are deleted first, inputs sorted
/// ordinally, node ids appended after the existing max, FTS optimize + VACUUM at the end.
/// </summary>
public static class RulesIngest
{
    public const string Schema = """
        CREATE TABLE rule(id TEXT PRIMARY KEY, title TEXT NOT NULL, statement TEXT NOT NULL, category TEXT NOT NULL, trust TEXT NOT NULL,
            formula_json TEXT, rationale TEXT, inferred_by TEXT, verification_method TEXT, verification_result TEXT, verification_detail TEXT,
            engine_status TEXT NOT NULL, engine_note TEXT, engine_refs_json TEXT NOT NULL, tags_json TEXT NOT NULL, value_json TEXT);
        CREATE TABLE rule_evidence(rule_id TEXT NOT NULL REFERENCES rule(id), ord INTEGER NOT NULL, source TEXT NOT NULL, locator TEXT NOT NULL,
            note TEXT NOT NULL, source_file_id INTEGER REFERENCES source_file(id), PRIMARY KEY(rule_id, ord));
        """;

    private static readonly string[] OwnNodeKinds = ["rule", "source_file", "project_default", "workbook_sheet"];
    private static readonly string[] OwnEdgeRels = ["governs", "evidenced_by"];
    private const string HsRoot = @"C:\HS-STEEL\";

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static RulesIngestReport Apply(string dbPath, RulesCatalog? catalog = null)
    {
        catalog ??= RulesCatalog.Instance;
        var o = StringComparer.Ordinal;
        var rules = catalog.All.OrderBy(r => r.Id, o).ToList();
        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(Path.GetFullPath(dbPath), SqliteOpenMode.ReadWrite));
        cn.Open();

        var sourceFiles = new Dictionary<string, (long Id, string Sha)>(o);
        foreach (var r in Rows(cn, null, "SELECT id, rel_path, sha256 FROM source_file"))
        {
            sourceFiles[(string)r[1]!] = ((long)r[0]!, (string)r[2]!);
        }

        var pdKeys = new HashSet<string>(o);
        foreach (var r in Rows(cn, null, "SELECT f.rel_path, p.key FROM project_default p JOIN source_file f ON f.id = p.source_file_id"))
        {
            pdKeys.Add((string)r[0]! + "#" + (string)r[1]!);
        }

        var sheets = new HashSet<string>(o);
        foreach (var r in Rows(cn, null, "SELECT f.rel_path, s.name FROM workbook_sheet s JOIN source_file f ON f.id = s.source_file_id"))
        {
            sheets.Add((string)r[0]! + "!" + (string)r[1]!);
        }

        using var tx = cn.BeginTransaction();
        Exec(cn, tx, "DROP TABLE IF EXISTS rule_evidence; DROP TABLE IF EXISTS rule;");
        Exec(cn, tx, Schema);
        Exec(cn, tx, "DELETE FROM edge WHERE rel IN (" + string.Join(",", OwnEdgeRels.Select(r => "'" + r + "'")) + ")");
        Exec(cn, tx, "DELETE FROM node WHERE kind IN (" + string.Join(",", OwnNodeKinds.Select(r => "'" + r + "'")) + ")");
        Exec(cn, tx, "DELETE FROM search_fts WHERE kind = 'rule'");

        var existing = new Dictionary<(string, string), long>();
        foreach (var r in Rows(cn, tx, "SELECT id, kind, key FROM node"))
        {
            existing[((string)r[1]!, (string)r[2]!)] = (long)r[0]!;
        }

        var boltKeys = existing.Keys.Where(k => k.Item1 == "bolt").Select(k => k.Item2).OrderBy(k => k, o).ToList();
        var newNodes = new SortedDictionary<(string Kind, string Key), (string Label, string Props)>(Comparer<(string, string)>.Create((a, b) =>
        {
            var c = o.Compare(a.Item1, b.Item1);
            return c != 0 ? c : o.Compare(a.Item2, b.Item2);
        }));
        var edges = new List<(string SK, string SKey, string Rel, string DK, string DKey, string? Ev, string? Note)>();
        var ruleRows = new List<object?[]>();
        var evRows = new List<object?[]>();
        var fts = new List<object?[]>();

        foreach (var r in rules)
        {
            var ver = r.Verification;
            ruleRows.Add([r.Id, r.Title, r.Statement, r.Category, r.Trust, J(r.Formula), r.Rationale, r.InferredBy, ver?.Method, ver?.Result, ver?.Detail,
                r.EngineStatus, r.EngineNote, JsonSerializer.Serialize(r.EngineRefs, Compact), JsonSerializer.Serialize(r.Tags, Compact), J(r.Value)]);
            newNodes[("rule", r.Id)] = (r.Title, JsonSerializer.Serialize(new SortedDictionary<string, object?>(o)
            {
                ["category"] = r.Category, ["trust"] = r.Trust, ["engine_status"] = r.EngineStatus, ["verification"] = ver?.Result ?? "unverified",
            }, Compact));

            var targets = new SortedSet<(string Kind, string Key, string Note)>();
            for (var i = 0; i < r.Evidence.Count; i++)
            {
                var e = r.Evidence[i];
                var rel = RelPath(e.Source);
                long? fid = rel is not null && sourceFiles.TryGetValue(rel, out var sf) ? sf.Id : null;
                evRows.Add([r.Id, i + 1, e.Source, e.Locator, e.Note, fid]);
                var fileKey = rel ?? e.Source.Replace('\\', '/');
                newNodes.TryAdd(("source_file", fileKey), (Path.GetFileName(fileKey.TrimEnd('/')), JsonSerializer.Serialize(new SortedDictionary<string, object?>(o)
                {
                    ["in_manifest"] = fid is not null, ["sha256"] = fid is not null ? sourceFiles[rel!].Sha : null,
                }, Compact)));
                edges.Add(("rule", r.Id, "evidenced_by", "source_file", fileKey, rel is not null && fid is not null ? rel : null, e.Locator));

                if (rel is null)
                {
                    continue;
                }

                if (e.Locator.StartsWith("key=", StringComparison.Ordinal) && pdKeys.Contains(rel + "#" + e.Locator[4..]))
                {
                    targets.Add(("project_default", rel + "#" + e.Locator[4..], e.Locator));
                }

                var bang = e.Locator.IndexOf('!');
                if (bang > 0 && sheets.Contains(rel + "!" + e.Locator[..bang]))
                {
                    targets.Add(("workbook_sheet", rel + "!" + e.Locator[..bang], e.Locator));
                }
            }

            foreach (var g in r.Governs ?? [])
            {
                var c = g.IndexOf(':');
                if (c <= 0)
                {
                    continue;
                }

                var (kind, key) = (g[..c], g[(c + 1)..]);
                if (kind == "bolt" && key.StartsWith('*'))
                {
                    foreach (var b in boltKeys.Where(b => b.StartsWith(key[1..] + " ", StringComparison.Ordinal)))
                    {
                        targets.Add(("bolt", b, "governs " + g));
                    }
                }
                else if ((kind == "workbook_sheet" && sheets.Contains(key)) || (kind == "project_default" && pdKeys.Contains(key)) || existing.ContainsKey((kind, key)))
                {
                    targets.Add((kind, key, "governs " + g));
                }
            }

            foreach (var t in targets.DistinctBy(t => (t.Kind, t.Key)))
            {
                if (t.Kind is "workbook_sheet" or "project_default")
                {
                    newNodes.TryAdd((t.Kind, t.Key), (t.Key[(t.Key.LastIndexOfAny(['/', '!', '#']) + 1)..], "{}"));
                }

                edges.Add(("rule", r.Id, "governs", t.Kind, t.Key, null, t.Note));
            }

            var formula = r.Formula is { } f && f.TryGetProperty("expr", out var ex) ? ex.GetString() : null;
            fts.Add(["rule", r.Id, $"{r.Id} {r.Title}",
                string.Join(' ', new[] { r.Statement, r.Category, "trust:" + r.Trust, "engine:" + r.EngineStatus, formula ?? "", ver?.Detail ?? "", r.Rationale ?? "", r.EngineNote ?? "" }
                    .Concat(r.Tags).Concat(r.Evidence.Select(e => e.Locator)).Where(s => s.Length > 0))]);
        }

        Insert(cn, tx, "INSERT INTO rule VALUES($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k,$l,$m,$n,$o,$p)", ruleRows);
        Insert(cn, tx, "INSERT INTO rule_evidence VALUES($a,$b,$c,$d,$e,$f)", evRows);

        var next = existing.Count == 0 ? 1 : existing.Values.Max() + 1;
        var nodeId = new Dictionary<(string, string), long>(existing);
        var added = new List<object?[]>();
        foreach (var n in newNodes)
        {
            if (!nodeId.ContainsKey(n.Key))
            {
                nodeId[n.Key] = next;
                added.Add([next, n.Key.Kind, n.Key.Key, n.Value.Label, n.Value.Props]);
                next++;
            }
        }

        Insert(cn, tx, "INSERT INTO node VALUES($a,$b,$c,$d,$e)", added);
        var edgeRows = edges.Where(e => nodeId.ContainsKey((e.SK, e.SKey)) && nodeId.ContainsKey((e.DK, e.DKey)))
            .Select(e => (Src: nodeId[(e.SK, e.SKey)], e.Rel, Dst: nodeId[(e.DK, e.DKey)], e.Ev, e.Note))
            .Distinct()
            .OrderBy(e => e.Src).ThenBy(e => e.Rel, o).ThenBy(e => e.Dst).ThenBy(e => e.Ev, o).ThenBy(e => e.Note, o)
            .ToList();
        Insert(cn, tx, "INSERT INTO edge VALUES($a,$b,$c,$d,$e)", edgeRows.Select(e => new object?[] { e.Src, e.Rel, e.Dst, e.Ev, e.Note }));

        long nextRow = (long)Rows(cn, tx, "SELECT COALESCE(MAX(rowid), 0) FROM search_fts")[0][0]! + 1;
        Insert(cn, tx, "INSERT INTO search_fts(rowid, kind, key, label, body) VALUES($a,$b,$c,$d,$e)",
            fts.Select((f, i) => new object?[] { nextRow + i, f[0], f[1], f[2], f[3] }));
        var trust = rules.GroupBy(r => r.Trust).OrderBy(g => g.Key, o).ToDictionary(g => g.Key, g => g.Count(), o);
        Insert(cn, tx, "INSERT OR REPLACE INTO meta VALUES($a,$b)",
        [
            ["rules_schema", "hs-steel-rules/2"],
            ["rules_trust_counts", JsonSerializer.Serialize(trust, Compact)],
        ]);
        tx.Commit();

        Exec(cn, null, "INSERT INTO search_fts(search_fts) VALUES('optimize');");
        Exec(cn, null, "VACUUM;");
        return new RulesIngestReport(rules.Count, evRows.Count, added.Count,
            edgeRows.Count(e => e.Rel == "governs"), edgeRows.Count(e => e.Rel == "evidenced_by"), trust);
    }

    /// <summary>C:\HS-STEEL\a\b.xlsx → a/b.xlsx (manifest rel path); null for repo/REBORN paths.</summary>
    public static string? RelPath(string source) =>
        source.StartsWith(HsRoot, StringComparison.OrdinalIgnoreCase) ? source[HsRoot.Length..].Replace('\\', '/') : null;

    private static string? J(JsonElement? e) => e is { } v ? JsonSerializer.Serialize(v, Compact) : null;

    private static List<object?[]> Rows(SqliteConnection cn, SqliteTransaction? tx, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        using var rd = cmd.ExecuteReader();
        var list = new List<object?[]>();
        while (rd.Read())
        {
            var row = new object?[rd.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = rd.IsDBNull(i) ? null : rd.GetValue(i);
            }

            list.Add(row);
        }

        return list;
    }

    private static void Exec(SqliteConnection cn, SqliteTransaction? tx, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static readonly string[] ParamNames = Enumerable.Range(0, 26).Select(i => "$" + (char)('a' + i)).ToArray();

    private static void Insert(SqliteConnection cn, SqliteTransaction tx, string sql, IEnumerable<object?[]> rows)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        SqliteParameter[]? ps = null;
        foreach (var row in rows)
        {
            if (ps is null)
            {
                ps = row.Select((_, i) => cmd.Parameters.Add(new SqliteParameter(ParamNames[i], null))).ToArray();
                cmd.Prepare();
            }

            for (var i = 0; i < row.Length; i++)
            {
                ps[i].Value = row[i] ?? DBNull.Value;
            }

            cmd.ExecuteNonQuery();
        }
    }
}
