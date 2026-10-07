using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace HsSteel.Knowledge.Reborn;

/// <summary>Result of <see cref="RebornIngest.Apply"/>.</summary>
public sealed record RebornReport(
    int DerivedSpecs, IReadOnlyDictionary<string, int> TrustCounts, int MdChunks, int Nodes, int Edges,
    IReadOnlyDictionary<string, int> EdgesByKind, WeightCrossCheck Weight)
{
    public string Summary =>
        $"reborn: derived_spec={DerivedSpecs} ({string.Join(", ", TrustCounts.Select(t => $"{t.Key}={t.Value}"))}) reborn_md_chunks={MdChunks} nodes+={Nodes} edges+={Edges} " +
        $"({string.Join(", ", EdgesByKind.Select(t => $"{t.Key}={t.Value}"))}) weight: {Weight}";
}

/// <summary>REBORN weight_table.json vs our section table (unit weight kg/m), matched on normalized spec.</summary>
public sealed record WeightCrossCheck(int RebornRows, int Matched, int Agree, int Disagree, double AgreementPct, double Tolerance, IReadOnlyList<string> SampleDisagreements)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"rows={RebornRows} matched={Matched} agree={Agree} disagree={Disagree} agreement={AgreementPct:F1}% (tol {Tolerance:P1})");
}

/// <summary>
/// Post-build step: adds derived_spec (+trust), reborn_md doc chunks, evidence-backed graph edges and FTS rows to an
/// hs_assets.db produced by <see cref="KnowledgeDbBuilder"/>. Deterministic: inputs sorted ordinally, ids appended after the
/// existing max, no timestamps, FTS optimize + VACUUM at the end.
/// </summary>
public static class RebornIngest
{
    public const string RootPrefix = "HS-STEEL_REBORN/";
    public const double WeightTolerance = 0.005; // 0.5 % relative (or 0.01 kg/m absolute)

    private const string DerivedSpecSchema = """
        CREATE TABLE derived_spec(id INTEGER PRIMARY KEY, source_file TEXT NOT NULL UNIQUE, source_sha256 TEXT NOT NULL, topic TEXT NOT NULL,
            trust TEXT NOT NULL CHECK(trust IN ('verified','unverified','refuted')), trust_note TEXT NOT NULL, json TEXT NOT NULL);
        """;

    private static readonly string[] MdFiles = ["ARCHITECTURE.md", "ORCHESTRATION_PLAN.md", "orch/user_manual.md"];

    public static RebornReport Apply(string dbPath, string rebornRoot)
    {
        var root = Path.GetFullPath(rebornRoot);
        var o = StringComparer.Ordinal;
        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(Path.GetFullPath(dbPath), SqliteOpenMode.ReadWrite));
        cn.Open();
        Exec(cn, DerivedSpecSchema);

        var existingNodes = new Dictionary<(string, string), long>();
        using (var c = cn.CreateCommand())
        {
            c.CommandText = "SELECT id, kind, key FROM node";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                existingNodes[(r.GetString(1), r.GetString(2))] = r.GetInt64(0);
            }
        }

        var newNodes = new SortedDictionary<(string Kind, string Key), (string Label, string Props)>(Comparer<(string, string)>.Create((a, b) =>
        {
            var x = o.Compare(a.Item1, b.Item1);
            return x != 0 ? x : o.Compare(a.Item2, b.Item2);
        }));
        var edges = new List<(string SK, string SKey, string Rel, string DK, string DKey, string Ev, string Note)>();
        var fts = new List<(string Kind, string Key, string Label, string Body)>();

        // ---- derived_spec
        var specs = new List<(string Rel, string Sha, RebornTrust.Grade G, string Json)>();
        var docs = new Dictionary<string, JsonDocument>(o);
        foreach (var (rel, g) in RebornTrust.Files)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(path);
            var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true });
            }
            catch (JsonException)
            {
                continue;
            }

            docs[rel] = doc;
            specs.Add((rel, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), g, text));
        }

        // ---- graph links (evidence-backed only)
        var commands = Names(cn, "SELECT name FROM command");
        var blocks = Names(cn, "SELECT name FROM block");
        var lispKeys = Names(cn, "SELECT key FROM node WHERE kind = 'lisp'");
        var cmdMacros = new List<(string Name, string Macro)>();
        using (var c = cn.CreateCommand())
        {
            c.CommandText = "SELECT name, macro FROM command WHERE macro IS NOT NULL ORDER BY name";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                cmdMacros.Add((r.GetString(0), r.GetString(1)));
            }
        }

        // catalog commands -> command nodes
        if (docs.TryGetValue("catalog/commands.json", out var cat))
        {
            const string ev = RootPrefix + "catalog/commands.json";
            var byLower = commands.GroupBy(n => n.ToLowerInvariant(), o).ToDictionary(g => g.Key, g => g.OrderBy(x => x, o).ToList(), o);
            foreach (var p in cat.RootElement.EnumerateObject().OrderBy(p => p.Name, o))
            {
                var e = p.Value;
                var ui = Str(e, "ui_name");
                var help = Str(e, "help");
                var group = Str(e, "group");
                newNodes[("catalog_entry", p.Name)] = (p.Name, Json(new SortedDictionary<string, object?> { ["ui_name"] = ui, ["help"] = help, ["group"] = group, ["source"] = Str(e, "source") }));
                fts.Add(("catalog_entry", p.Name, p.Name, $"{ui} {help} {group} {Str(e, "command")}"));
                var lower = (Str(e, "command") ?? p.Name).ToLowerInvariant();
                if (byLower.TryGetValue(lower, out var hits))
                {
                    foreach (var h in hits)
                    {
                        edges.Add(("command", h, "described_by", "catalog_entry", p.Name, ev, "command name equals catalog key (case-insensitive)"));
                    }

                    continue;
                }

                // unique command whose macro invokes exactly this command token (^C^C_name / ^C^Cname)
                var tok = new Regex(@"\^C\^C_?" + Regex.Escape(lower) + @"(?![A-Za-z0-9_\-$])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var viaMacro = cmdMacros.Where(m => tok.IsMatch(m.Macro)).Select(m => m.Name).Distinct(o).ToList();
                if (viaMacro.Count == 1)
                {
                    edges.Add(("command", viaMacro[0], "described_by", "catalog_entry", p.Name, ev, "unique menu macro invokes ^C^C" + lower));
                }
            }
        }

        // FAS functions -> lisp nodes (lisp fn names from menu macros)
        if (docs.TryGetValue("orch/fas_symbols.json", out var fas) && fas.RootElement.TryGetProperty("symbols", out var sy) && sy.TryGetProperty("functions", out var fns))
        {
            const string ev = RootPrefix + "orch/fas_symbols.json";
            var lispByLower = lispKeys.GroupBy(k => k.ToLowerInvariant(), o).ToDictionary(g => g.Key, g => g.ToList(), o);
            foreach (var fn in fns.EnumerateArray().Select(x => x.GetString()).OfType<string>().Distinct(o).OrderBy(x => x, o))
            {
                newNodes[("fas_function", fn)] = (fn, Json(new SortedDictionary<string, object?> { ["source"] = "hs02.VLX (FAS string scrape)" }));
                if (lispByLower.TryGetValue(fn.ToLowerInvariant(), out var ls))
                {
                    foreach (var l in ls)
                    {
                        edges.Add(("lisp", l, "described_by", "fas_function", fn, ev, "menu-macro lisp call name equals FAS function symbol"));
                    }
                }
            }

            // FAS command symbols "NAMEfn" → command node NAME when NAME is an existing command
            if (fas.RootElement.TryGetProperty("commands", out var fc))
            {
                var cmdLower = commands.GroupBy(n => n.ToLowerInvariant(), o).ToDictionary(g => g.Key, g => g.OrderBy(x => x, o).ToList(), o);
                var fnSet = fns.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToHashSet(o);
                foreach (var raw in fc.EnumerateArray().Select(x => x.GetString()).OfType<string>().Distinct(o).OrderBy(x => x, o))
                {
                    var fn = fnSet.Where(f => raw.Length > f.Length && raw.EndsWith(f, StringComparison.Ordinal)).OrderByDescending(f => f.Length).FirstOrDefault();
                    if (fn is null)
                    {
                        continue;
                    }

                    var name = raw[..^fn.Length];
                    if (cmdLower.TryGetValue(name.ToLowerInvariant(), out var cs))
                    {
                        foreach (var cmdName in cs)
                        {
                            edges.Add(("command", cmdName, "implemented_by", "fas_function", fn, ev, "FAS defun C:" + name + " -> " + fn));
                        }
                    }
                }
            }
        }

        // dwg_blocks_v2 per-file entries -> block nodes (basename match)
        var blockMatch = 0;
        var dxfCount = 0;
        if (docs.TryGetValue("orch/dwg_blocks_v2.json", out var dwg) && dwg.RootElement.TryGetProperty("per_file", out var pf))
        {
            const string ev = RootPrefix + "orch/dwg_blocks_v2.json";
            var blockLower = blocks.GroupBy(b => b.ToLowerInvariant(), o).ToDictionary(g => g.Key, g => g.OrderBy(x => x, o).ToList(), o);
            foreach (var p in pf.EnumerateObject().OrderBy(p => p.Name, o))
            {
                dxfCount++;
                var props = new SortedDictionary<string, object?>(o);
                if (p.Value.TryGetProperty("entity_counts", out var ec) && ec.ValueKind == JsonValueKind.Object)
                {
                    props["entity_counts"] = ec.EnumerateObject().OrderBy(x => x.Name, o).ToDictionary(x => x.Name, x => (object?)x.Value.ToString(), o);
                }

                props["blocks_real"] = p.Value.TryGetProperty("blocks_real", out var br) ? br.ToString() : null;
                newNodes[("dwg_dxf_entry", p.Name)] = (p.Name, Json(props));
                var stem = Path.GetFileNameWithoutExtension(p.Name).ToLowerInvariant();
                if (blockLower.TryGetValue(stem, out var bs))
                {
                    blockMatch++;
                    foreach (var b in bs)
                    {
                        edges.Add(("block", b, "described_by", "dwg_dxf_entry", p.Name, ev, "DXF basename equals block name (converted from HSSTEEL/block/" + b + ".dwg)"));
                    }
                }
            }
        }

        // BOM sheet nodes with columns
        foreach (var (rel, sheetsPath) in new[] { ("orch/bom_schema.json", new[] { "schema", "sheets" }) })
        {
            if (!docs.TryGetValue(rel, out var bom))
            {
                continue;
            }

            var el = bom.RootElement;
            var ok = true;
            foreach (var k in sheetsPath)
            {
                ok = ok && el.TryGetProperty(k, out el);
            }

            if (!ok)
            {
                continue;
            }

            var order = bom.RootElement.TryGetProperty("sheet_list", out var sl) ? sl.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
            foreach (var sheet in el.EnumerateObject().OrderBy(s => s.Name, o))
            {
                var cols = new List<string>();
                if (sheet.Value.TryGetProperty("header_rows", out var hr))
                {
                    foreach (var row in hr.EnumerateObject().OrderBy(r => RowNum(r.Name)))
                    {
                        foreach (var cell in row.Value.EnumerateObject().OrderBy(c => ColNum(c.Name)))
                        {
                            if (cell.Value.ValueKind == JsonValueKind.String && !cell.Value.GetString()!.StartsWith('='))
                            {
                                cols.Add(row.Name + "!" + cell.Name + "=" + Regex.Replace(cell.Value.GetString()!, @"\s+", " ").Trim());
                            }
                        }
                    }
                }

                newNodes[("bom_sheet", sheet.Name)] = (sheet.Name, Json(new SortedDictionary<string, object?>
                {
                    ["columns"] = cols, ["sheet_index"] = order.IndexOf(sheet.Name) + 1,
                    ["data_start_row"] = sheet.Value.TryGetProperty("data_start_row", out var ds) ? ds.ToString() : null,
                }));
                edges.Add(("bom_sheet", sheet.Name, "described_by", "derived_spec", rel, RootPrefix + rel, "sheet '" + sheet.Name + "' in schema.sheets of original 9-sheet BOM xlsm"));
                if (sheet.Name is "BomList" or "AssyList")
                {
                    edges.Add(("bom_sheet", sheet.Name, "described_by", "derived_spec", "orch/bomlist_spec.json", RootPrefix + "orch/bomlist_spec.json", "bomlist_spec." + sheet.Name.ToLowerInvariant()));
                }

                fts.Add(("bom_sheet", sheet.Name, sheet.Name, "BOM 자재 워크북 시트 " + string.Join(' ', cols)));
            }
        }

        // ---- weight cross-check
        var weight = CrossCheckWeights(cn, docs.GetValueOrDefault("materials/weight_table.json"));

        // finalize trust (dynamic upgrades/notes from our own checks)
        var specRows = new List<(string Rel, string Sha, string Topic, string Trust, string Note, string Json)>();
        foreach (var s in specs.OrderBy(s => s.Rel, o))
        {
            var trust = s.G.Trust;
            var note = s.G.Note;
            if (s.Rel == "orch/dwg_blocks_v2.json" && dxfCount > 0)
            {
                note += $" Inventory check (this ingest): {blockMatch}/{dxfCount} per_file basenames are blocks in our block table.";
                if (blockMatch == dxfCount)
                {
                    trust = RebornTrust.Verified;
                    note += " File inventory verified; entity counts not re-checked.";
                }
            }
            else if (s.Rel == "materials/weight_table.json")
            {
                note += " Cross-check: " + weight + ".";
            }

            specRows.Add((s.Rel, s.Sha, s.G.Topic, trust, note, s.Json));
        }

        // ---- md chunks
        var chunks = new List<(string Id, string Rel, int Page, string Head, string Text, string Sha)>();
        foreach (var rel in MdFiles)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(path);
            var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿').Replace("\r\n", "\n");
            var i = 0;
            foreach (var (head, body) in ChunkMarkdown(text))
            {
                i++;
                chunks.Add(($"reborn/{rel}#{i:D3}", rel, i, head, body, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(body)))));
            }
        }

        // ---- write
        using (var tx = cn.BeginTransaction())
        {
            Insert(cn, tx, "INSERT INTO derived_spec VALUES($a,$b,$c,$d,$e,$f,$g)",
                specRows.Select((s, i) => new object?[] { i + 1, RootPrefix + s.Rel, s.Sha, s.Topic, s.Trust, s.Note, s.Json }));
            foreach (var s in specRows)
            {
                newNodes[("derived_spec", s.Rel)] = (s.Topic, Json(new SortedDictionary<string, object?>
                    { ["source_file"] = RootPrefix + s.Rel, ["trust"] = s.Trust, ["trust_note"] = s.Note }));
                fts.Add(("derived_spec", s.Rel, s.Topic, $"{s.Rel} trust:{s.Trust} {s.Note} {Flatten(s.Json, 30000)}"));
            }

            Insert(cn, tx, "INSERT INTO doc_chunk VALUES($a,$b,$c,$d,$e,$f,$g)",
                chunks.OrderBy(c => c.Id, o).Select(c => new object?[] { c.Id, null, c.Page, c.Text, RootPrefix + c.Rel + " § " + c.Head, "reborn_md", c.Sha }));
            foreach (var c in chunks)
            {
                fts.Add(("doc_chunk", c.Id, $"{RootPrefix}{c.Rel} § {c.Head}", c.Text + QueryExpand.EnrichmentFor(c.Text)));
            }

            var nextNode = existingNodes.Count == 0 ? 1 : existingNodes.Values.Max() + 1;
            var nodeId = new Dictionary<(string, string), long>(existingNodes);
            var added = new List<object?[]>();
            foreach (var n in newNodes)
            {
                if (nodeId.ContainsKey(n.Key))
                {
                    continue;
                }

                nodeId[n.Key] = nextNode;
                added.Add([nextNode, n.Key.Kind, n.Key.Key, n.Value.Label, n.Value.Props]);
                nextNode++;
            }

            Insert(cn, tx, "INSERT INTO node VALUES($a,$b,$c,$d,$e)", added);
            var edgeRows = edges.Where(e => nodeId.ContainsKey((e.SK, e.SKey)) && nodeId.ContainsKey((e.DK, e.DKey)))
                .Select(e => (Src: nodeId[(e.SK, e.SKey)], e.Rel, Dst: nodeId[(e.DK, e.DKey)], e.Ev, e.Note, Kind: e.SK + "->" + e.DK))
                .Distinct()
                .OrderBy(e => e.Src).ThenBy(e => e.Rel, o).ThenBy(e => e.Dst).ThenBy(e => e.Ev, o).ThenBy(e => e.Note, o)
                .ToList();
            Insert(cn, tx, "INSERT INTO edge VALUES($a,$b,$c,$d,$e)", edgeRows.Select(e => new object?[] { e.Src, e.Rel, e.Dst, e.Ev, e.Note }));

            long nextRow;
            using (var c = cn.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "SELECT COALESCE(MAX(rowid), 0) FROM search_fts";
                nextRow = (long)c.ExecuteScalar()! + 1;
            }

            Insert(cn, tx, "INSERT INTO search_fts(rowid, kind, key, label, body) VALUES($a,$b,$c,$d,$e)",
                fts.OrderBy(f => f.Kind, o).ThenBy(f => f.Key, o).Select((f, i) => new object?[] { nextRow + i, f.Kind, f.Key, f.Label, f.Body }));

            var trustCounts = specRows.GroupBy(s => s.Trust).OrderBy(g => g.Key, o).ToDictionary(g => g.Key, g => g.Count(), o);
            Insert(cn, tx, "INSERT OR REPLACE INTO meta VALUES($a,$b)",
            [
                ["reborn_schema", "hs-reborn/1"],
                ["reborn_weight_crosscheck", Json(weight)],
                ["reborn_trust_counts", Json(trustCounts)],
            ]);
            tx.Commit();

            foreach (var d in docs.Values)
            {
                d.Dispose();
            }

            Exec(cn, "INSERT INTO search_fts(search_fts) VALUES('optimize');");
            Exec(cn, "VACUUM;");
            var byKind = edgeRows.GroupBy(e => e.Kind).OrderBy(g => g.Key, o).ToDictionary(g => g.Key, g => g.Count(), o);
            return new RebornReport(specRows.Count, trustCounts, chunks.Count, added.Count, edgeRows.Count, byKind, weight);
        }
    }

    /// <summary>Normalized spec key: upper, '*'/'×'→X, no spaces/dashes.</summary>
    public static string SpecKey(string spec) => SpecAliases.Normalize(spec).Replace("-", string.Empty, StringComparison.Ordinal);

    private static WeightCrossCheck CrossCheckWeights(SqliteConnection cn, JsonDocument? table)
    {
        if (table is null || table.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new WeightCrossCheck(0, 0, 0, 0, 0, WeightTolerance, []);
        }

        var ours = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        using (var c = cn.CreateCommand())
        {
            c.CommandText = "SELECT spec, unit_weight FROM section ORDER BY key";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                var k = SpecKey(r.GetString(0));
                if (!ours.TryGetValue(k, out var l))
                {
                    ours[k] = l = [];
                }

                l.Add(r.GetDouble(1));
            }
        }

        int rows = 0, matched = 0, agree = 0;
        var bad = new List<string>();
        foreach (var e in table.RootElement.EnumerateArray())
        {
            rows++;
            var spec = Str(e, "spec");
            if (spec is null || !e.TryGetProperty("unit_weight", out var uw) || uw.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            if (!ours.TryGetValue(SpecKey(spec), out var cand))
            {
                continue;
            }

            matched++;
            var w = uw.GetDouble();
            if (cand.Any(x => Math.Abs(x - w) <= Math.Max(0.01, WeightTolerance * Math.Abs(x))))
            {
                agree++;
            }
            else
            {
                bad.Add(string.Create(CultureInfo.InvariantCulture, $"{spec}: reborn {w} vs ours {string.Join('/', cand)}"));
            }
        }

        var pct = matched == 0 ? 0 : Math.Round(100.0 * agree / matched, 2);
        return new WeightCrossCheck(rows, matched, agree, matched - agree, pct, WeightTolerance, bad.OrderBy(x => x, StringComparer.Ordinal).Take(20).ToList());
    }

    /// <summary>Splits on markdown headings (#, ##, ###); long sections are split at ~1500 chars on blank lines.</summary>
    public static IEnumerable<(string Head, string Body)> ChunkMarkdown(string text)
    {
        var head = "(intro)";
        var sb = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (Regex.IsMatch(line, "^#{1,3} "))
            {
                foreach (var part in Flush(head, sb.ToString()))
                {
                    yield return part;
                }

                sb.Clear();
                head = line.TrimStart('#').Trim();
            }

            sb.Append(line).Append('\n');
        }

        foreach (var part in Flush(head, sb.ToString()))
        {
            yield return part;
        }

        static IEnumerable<(string, string)> Flush(string h, string body)
        {
            body = body.Trim();
            if (body.Length == 0)
            {
                yield break;
            }

            var cur = new StringBuilder();
            foreach (var para in body.Split("\n\n"))
            {
                if (cur.Length > 0 && cur.Length + para.Length > 1500)
                {
                    yield return (h, cur.ToString().Trim());
                    cur.Clear();
                }

                cur.Append(para).Append("\n\n");
            }

            if (cur.ToString().Trim().Length > 0)
            {
                yield return (h, cur.ToString().Trim());
            }
        }
    }

    /// <summary>Distinct property names and string values of a JSON text, in document order, capped.</summary>
    private static string Flatten(string json, int cap)
    {
        using var d = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        void Add(string s)
        {
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length is > 0 and < 400 && sb.Length < cap && seen.Add(s))
            {
                sb.Append(s).Append(' ');
            }
        }

        void Walk(JsonElement e)
        {
            if (sb.Length >= cap)
            {
                return;
            }

            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        Add(p.Name);
                        Walk(p.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray())
                    {
                        Walk(x);
                    }

                    break;
                case JsonValueKind.String:
                    Add(e.GetString()!);
                    break;
            }
        }

        Walk(d.RootElement);
        return sb.ToString();
    }

    private static int RowNum(string s) => int.TryParse(new string(s.Where(char.IsDigit).ToArray()), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static int ColNum(string s) => s.Aggregate(0, (a, ch) => char.IsLetter(ch) ? a * 26 + (char.ToUpperInvariant(ch) - 'A' + 1) : a);

    private static string? Str(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Names(SqliteConnection cn, string sql)
    {
        using var c = cn.CreateCommand();
        c.CommandText = sql;
        using var r = c.ExecuteReader();
        var l = new List<string>();
        while (r.Read())
        {
            l.Add(r.GetString(0));
        }

        l.Sort(StringComparer.Ordinal);
        return l;
    }

    private static readonly JsonSerializerOptions CompactJson = new(Manifest.JsonOptions) { WriteIndented = false };

    private static string Json(object v) => JsonSerializer.Serialize(v, CompactJson);

    private static void Exec(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
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
