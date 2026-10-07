using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HsSteel.Drafting;
using HsSteel.Knowledge;
using HsSteel.Modeling;
using Microsoft.Data.Sqlite;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace HsSteel.Mcp;

/// <summary>Location of the knowledge DB (env HS_ASSETS_DB, default out/hs_assets.db) and of the legacy HS-STEEL tree.</summary>
public sealed record AssetOptions(string DbPath, string LegacyRoot)
{
    public static AssetOptions FromEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("HS_ASSETS_DB");
        string? db = !string.IsNullOrWhiteSpace(env) ? env : null;
        if (db is null)
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (var d = new DirectoryInfo(start); d is not null && db is null; d = d.Parent)
                {
                    var c = Path.Combine(d.FullName, "out", "hs_assets.db");
                    if (File.Exists(c))
                    {
                        db = c;
                    }
                }
            }
        }

        return new AssetOptions(db ?? Path.GetFullPath(Path.Combine("out", "hs_assets.db")), ManifestScanner.DefaultRoot());
    }
}

internal static class AssetDb
{
    public static SqliteConnection Open(AssetOptions o)
    {
        if (!File.Exists(o.DbPath))
        {
            throw new McpException($"Asset DB not found: {o.DbPath}. Build it with the HsSteel.Knowledge tool or set HS_ASSETS_DB.");
        }

        var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(Path.GetFullPath(o.DbPath), SqliteOpenMode.ReadOnly));
        cn.Open();
        return cn;
    }

    public static List<object?[]> Rows(SqliteConnection cn, string sql, params (string, object?)[] ps)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps)
        {
            cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        }

        var list = new List<object?[]>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new object?[r.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            }

            list.Add(row);
        }

        return list;
    }

    /// <summary>(rel_path, sha256, size) of the file a node was read from, or null.</summary>
    public static (string Rel, string Sha, long Size)? SourceOf(SqliteConnection cn, string kind, string key)
    {
        var sql = kind switch
        {
            "section" => "SELECT f.rel_path, f.sha256, f.size FROM section x JOIN source_file f ON f.id = x.source_file_id WHERE x.key = $k",
            "bolt" => "SELECT f.rel_path, f.sha256, f.size FROM bolt x JOIN source_file f ON f.id = x.source_file_id WHERE x.spec = $k",
            "block" => "SELECT f.rel_path, f.sha256, f.size FROM block x JOIN source_file f ON f.id = x.source_file_id WHERE x.name = $k",
            "command" => "SELECT f.rel_path, f.sha256, f.size FROM command x JOIN source_file f ON f.id = x.source_file_id WHERE x.name = $k",
            "doc_chunk" => "SELECT f.rel_path, f.sha256, f.size FROM doc_chunk x JOIN source_file f ON f.id = x.source_file_id WHERE x.id = $k",
            _ => null,
        };
        if (sql is null)
        {
            return null;
        }

        var rows = Rows(cn, sql, ("$k", key));
        return rows.Count == 0 ? null : ((string)rows[0][0]!, (string)rows[0][1]!, Convert.ToInt64(rows[0][2], CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Asset knowledge tools (hs_assets.db) and the bridge to power-cad: block insert plans and cad_create_many payloads.
/// </summary>
[McpServerToolType]
public sealed class AssetTools(Workspace ws, AssetOptions opt)
{
    public const int MaxEntitiesPerCall = 5000;

    private static readonly JsonSerializerOptions JsonOut = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Json(JsonNode n) => n.ToJsonString(JsonOut);

    private static JsonNode? Props(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string AbsPath(string rel) => Path.GetFullPath(Path.Combine(opt.LegacyRoot, rel.Replace('/', Path.DirectorySeparatorChar)));

    [McpServerTool(Name = "hs_asset_search", ReadOnly = true, Idempotent = true)]
    [Description("Search the HS-STEEL asset knowledge base: exact spec/alias matches first, then hybrid full-text + vector (when ONNX model and chunk embeddings are present). Kinds: section, bolt, block, command, command_alias, doc_chunk, ... "
        + "Each hit has kind, key, label, score, matched_by and the legacy source file (relative path). mode: auto (default hybrid), lexical, semantic.")]
    public string AssetSearch(
        [Description("Spec, alias or free text, e.g. 'H-400x200', 'HTB M20'")] string query,
        [Description("Restrict to a kind, e.g. 'section' or 'block'")] string? kind = null,
        int limit = 10,
        [Description("auto | lexical | semantic")] string mode = "auto")
    {
        using var store = new KnowledgeStore(opt.DbPath);
        using var cn = AssetDb.Open(opt);
        if (!Enum.TryParse<SearchMode>(mode, true, out var searchMode))
        {
            throw new McpException($"Unknown search mode '{mode}'; use auto, lexical, or semantic.");
        }

        var hits = store.Search(query, kind, Math.Clamp(limit, 1, 100), searchMode);
        return Json(new JsonArray([.. hits.Select(h => (JsonNode)new JsonObject
        {
            ["kind"] = h.Kind, ["key"] = h.Key, ["label"] = h.Label, ["score"] = Math.Round(h.Score, 4), ["matched_by"] = h.MatchedBy,
            ["source_file"] = AssetDb.SourceOf(cn, h.Kind, h.Key)?.Rel,
        })]));
    }

    [McpServerTool(Name = "hs_asset_get", ReadOnly = true, Idempotent = true)]
    [Description("Full record of one asset: node properties, provenance (source_file relative path, sha256, size, absolute legacy path) and, for blocks, "
        + "geometry, attributes and layers. include_preview=true adds the block preview as SVG text.")]
    public string AssetGet(
        [Description("section | bolt | block | command | ...")] string kind,
        [Description("Key, e.g. 'H-BEAM/H400x200x8x13' or a block name")] string key,
        bool include_preview = false)
    {
        using var store = new KnowledgeStore(opt.DbPath);
        var node = store.Get(kind, key) ?? throw new McpException($"No {kind} '{key}' in the asset DB.");
        using var cn = AssetDb.Open(opt);
        var o = new JsonObject
        {
            ["kind"] = node.Kind, ["key"] = node.Key, ["label"] = node.Label, ["props"] = Props(node.PropsJson),
        };
        if (AssetDb.SourceOf(cn, kind, key) is { } s)
        {
            o["provenance"] = new JsonObject { ["source_file"] = s.Rel, ["sha256"] = s.Sha, ["size"] = s.Size, ["abs_path"] = AbsPath(s.Rel) };
        }

        if (kind == "block")
        {
            var b = AssetDb.Rows(cn, "SELECT id, base_x, base_y, base_z, ext_minx, ext_miny, ext_maxx, ext_maxy, entity_count, preview_svg, description, sha256 FROM block WHERE name = $n", ("$n", key));
            if (b.Count > 0)
            {
                var r = b[0];
                double D(int i) => Convert.ToDouble(r[i], CultureInfo.InvariantCulture);
                var id = Convert.ToInt64(r[0], CultureInfo.InvariantCulture);
                o["block"] = new JsonObject
                {
                    ["base"] = new JsonArray(D(1), D(2), D(3)),
                    ["extents"] = new JsonArray(D(4), D(5), D(6), D(7)),
                    ["entity_count"] = Convert.ToInt64(r[8], CultureInfo.InvariantCulture),
                    ["has_preview"] = r[9] is string,
                    ["description"] = (string?)r[10],
                    ["sha256"] = (string?)r[11],
                    ["attributes"] = new JsonArray([.. AssetDb.Rows(cn, "SELECT tag, prompt, default_value FROM block_attribute WHERE block_id = $i ORDER BY tag", ("$i", id))
                        .Select(a => (JsonNode)new JsonObject { ["tag"] = (string)a[0]!, ["prompt"] = (string?)a[1], ["default"] = (string?)a[2] })]),
                    ["layers"] = new JsonArray([.. AssetDb.Rows(cn, "SELECT layer FROM block_layer WHERE block_id = $i ORDER BY layer", ("$i", id)).Select(a => (JsonNode)(string)a[0]!)]),
                };
                if (include_preview && r[9] is string svg)
                {
                    o["preview_svg"] = svg;
                }
            }
        }

        return Json(o);
    }

    [McpServerTool(Name = "hs_graph_neighbors", ReadOnly = true, Idempotent = true)]
    [Description("Walk the asset knowledge graph from a node (both edge directions, breadth first). rel filters the relation, e.g. family, spliced_with, uses_layer, alias_of, places.")]
    public string GraphNeighbors(string kind, string key, string? rel = null, int depth = 1)
    {
        using var store = new KnowledgeStore(opt.DbPath);
        var node = store.Get(kind, key) ?? throw new McpException($"No {kind} '{key}' in the asset DB.");
        var hits = store.Neighbors(node.Id, rel, Math.Clamp(depth, 1, 4));
        return Json(new JsonObject
        {
            ["node"] = new JsonObject { ["kind"] = node.Kind, ["key"] = node.Key, ["label"] = node.Label },
            ["neighbors"] = new JsonArray([.. hits.Select(h => (JsonNode)new JsonObject
            {
                ["kind"] = h.Node.Kind, ["key"] = h.Node.Key, ["label"] = h.Node.Label, ["depth"] = h.Depth,
                ["rel"] = h.Edge.Rel, ["evidence_source_file"] = h.Edge.EvidenceSourceFile, ["evidence_note"] = h.Edge.EvidenceNote,
            })]),
        });
    }


    [McpServerTool(Name = "hs_graph_rag", ReadOnly = true, Idempotent = true)]
    [Description("Graph RAG: hybrid asset search, expand neighbors on the knowledge graph, and attach evidence-backed detailing rules. Returns a context pack for grounded answers.")]
    public string GraphRagQuery(
        [Description("Natural-language or keyword query (KO/EN)")] string query,
        [Description("Optional node kind filter for search (section, bolt, block, command, doc_chunk, …)")] string? kind = null,
        [Description("Search hit limit (default 8)")] int search_limit = 8,
        [Description("Graph expansion depth 0-3 (default 1)")] int expand_depth = 1,
        [Description("auto | lexical | semantic")] string mode = "auto")
    {
        using var store = new KnowledgeStore(opt.DbPath);
        var m = mode.Trim().ToLowerInvariant() switch
        {
            "lexical" or "lex" => SearchMode.Lexical,
            "semantic" or "vec" or "vector" => SearchMode.Semantic,
            _ => SearchMode.Auto,
        };
        var result = GraphRag.Query(store, query, kind, Math.Clamp(search_limit, 1, 40), Math.Clamp(expand_depth, 0, 3), mode: m);
        return GraphRag.ToJson(result).ToJsonString();
    }

    [McpServerTool(Name = "hs_explain", ReadOnly = true, Idempotent = true)]
    [Description("Explain a knowledge-graph node: props, multi-hop neighbors by relation, and related RULES_CATALOG entries with engine_refs.")]
    public string ExplainNode(
        [Description("Node kind (section, bolt, block, command, family, …)")] string kind,
        [Description("Node key (e.g. H-BEAM/H400x200x8x13)")] string key,
        [Description("Neighbor depth 1-4 (default 2)")] int depth = 2)
    {
        using var store = new KnowledgeStore(opt.DbPath);
        try
        {
            return HsExplain.Explain(store, kind, key, depth).Json.ToJsonString();
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "hs_rules_search", ReadOnly = true, Idempotent = true)]
    [Description("Search the evidence-backed RULES_CATALOG (Project.dat, weld/bolt gauges, shear-tab, SCSS, assembly marks, REBORN specs).")]
    public string RulesSearch(
        [Description("Query text")] string query,
        [Description("Max rules (default 15)")] int limit = 15)
    {
        var hits = HsSteel.Knowledge.Rules.RulesCatalog.Instance.Search(query, Math.Clamp(limit, 1, 50));
        return new JsonObject
        {
            ["count"] = hits.Count,
            ["catalog_size"] = HsSteel.Knowledge.Rules.RulesCatalog.Instance.Count,
            ["rules"] = new JsonArray([.. hits.Select(r => (JsonNode)new JsonObject
            {
                ["id"] = r.Id,
                ["title"] = r.Title,
                ["statement"] = r.Statement,
                ["category"] = r.Category,
                ["trust"] = r.Trust,
                ["verification"] = r.Verification is { } v ? $"{v.Method}:{v.Result} {v.Detail}".Trim() : null,
                ["engine_status"] = r.EngineStatus,
                ["formula"] = r.Formula is { } f ? JsonNode.Parse(f.GetRawText()) : null,
                ["tags"] = new JsonArray([.. r.Tags]),
                ["engine_refs"] = new JsonArray([.. r.EngineRefs]),
                ["evidence"] = new JsonArray([.. r.Evidence.Select(e => (JsonNode)new JsonObject { ["source"] = e.Source, ["locator"] = e.Locator, ["note"] = e.Note })]),
            })]),
        }.ToJsonString();
    }

    [McpServerTool(Name = "hs_block_insert_plan", ReadOnly = true, Idempotent = true)]
    [Description("Plan to place a legacy HS-STEEL block with power-cad: call cad_import_block with `import` {name, path}, then cad_create with `create`. "
        + "Block attribute tags are checked against the block definition (see warnings).")]
    public string BlockInsertPlan(
        [Description("Block name in the asset DB")] string block,
        double x, double y, double rotation = 0, double scale = 1,
        [Description("{TAG: value} block attributes")] Dictionary<string, string>? attributes = null)
    {
        using var cn = AssetDb.Open(opt);
        var rows = AssetDb.Rows(cn, "SELECT b.id, b.name, f.rel_path FROM block b JOIN source_file f ON f.id = b.source_file_id WHERE b.name = $n COLLATE NOCASE ORDER BY (b.name = $n) DESC LIMIT 1", ("$n", block));
        if (rows.Count == 0)
        {
            throw new McpException($"Block '{block}' not found; use hs_asset_search with kind=block.");
        }

        var id = Convert.ToInt64(rows[0][0], CultureInfo.InvariantCulture);
        var name = (string)rows[0][1]!;
        var path = AbsPath((string)rows[0][2]!);
        var warnings = new JsonArray();
        if (!File.Exists(path))
        {
            warnings.Add($"Legacy DWG not found on this machine: {path}");
        }

        var tags = AssetDb.Rows(cn, "SELECT tag FROM block_attribute WHERE block_id = $i", ("$i", id)).Select(r => (string)r[0]!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var create = new JsonObject { ["type"] = "insert", ["name"] = name, ["position"] = new JsonArray(x, y), ["rotation"] = rotation, ["scale"] = scale };
        if (attributes is { Count: > 0 })
        {
            var a = new JsonObject();
            foreach (var (k, v) in attributes)
            {
                a[k] = v;
                if (!tags.Contains(k))
                {
                    warnings.Add($"Block {name} has no attribute '{k}' (defined: {string.Join(", ", tags.Order())}).");
                }
            }

            create["attributes"] = a;
        }

        var o = new JsonObject
        {
            ["import"] = new JsonObject { ["name"] = name, ["path"] = path },
            ["create"] = create,
        };
        if (warnings.Count > 0)
        {
            o["warnings"] = warnings;
        }

        return Json(o);
    }

    [McpServerTool(Name = "hs_drawings_to_powercad", ReadOnly = true)]
    [Description("Convert the shop drawings of a project (name in the workspace, or a path to a .hsproj.json) into power-cad cad_create_many payload(s): "
        + "each payload has entities (<=5000, with per-entity hs tags kind/mark/spec/length/assembly/sheet), layers and xdata_app, ready to pass as the arguments of cad_create_many. "
        + "blocks_needed lists frame/detail blocks that must exist in the drawing first (cad_import_block) or pass skip_missing_blocks=true.")]
    public string DrawingsToPowerCad(
        [Description("Project name in the workspace or path to a project JSON file")] string project,
        [Description("Only this sheet, e.g. 'A-001'")] string? sheet = null,
        [Description("assembly | part | plate | plan | bom (default all)")] string[]? kinds = null)
    {
        var p = File.Exists(project) ? Project.FromJson(File.ReadAllText(project)) : ws.Load(project);
        var set = ws.Drawings(p, HsTools.ParseKinds(kinds));
        var sheets = sheet is null ? set.Sheets : set.Sheets.Where(s => s.Number == sheet).ToList();
        if (sheets.Count == 0)
        {
            throw new McpException(sheet is null ? "No sheets generated." : $"Sheet {sheet} not found. Available: {string.Join(", ", set.Sheets.Select(s => s.Number))}.");
        }

        var enrich = new TagEnricher(set.Model);
        var entities = new List<JsonNode>();
        var blocks = new SortedSet<string>(StringComparer.Ordinal);
        var layers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in sheets)
        {
            foreach (var e in s.Plan.Entities)
            {
                var o = e.Spec.DeepClone().AsObject();
                // power-cad rejects empty text (e.g. blank BOM cells); they draw nothing anyway.
                if (e.Type is "text" or "mtext" && string.IsNullOrWhiteSpace(o["text"]?.GetValue<string>()))
                {
                    continue;
                }

                o["hs"] = enrich.Enrich(e.Tag, s.Number);
                entities.Add(o);
                layers.Add(e.Layer);
                if (e.Type == "insert" && o["name"]?.GetValue<string>() is { } bn)
                {
                    blocks.Add(bn);
                }
            }
        }

        var layerDefs = new JsonArray([.. layers.Select(l =>
        {
            var d = new JsonObject { ["name"] = l };
            var def = Layers.All.FirstOrDefault(x => x.Name == l);
            if (def.Name is not null)
            {
                d["color"] = def.Color;
                d["linetype"] = def.Linetype;
            }

            return (JsonNode)d;
        })]);
        var payloads = new JsonArray();
        for (var i = 0; i < entities.Count || i == 0; i += MaxEntitiesPerCall)
        {
            var pl = new JsonObject { ["entities"] = new JsonArray([.. entities.Skip(i).Take(MaxEntitiesPerCall)]) };
            if (i == 0)
            {
                pl["layers"] = layerDefs.DeepClone();
                // cad_create_many creates these when missing (entities reference style HS-KOR). Existing styles are never changed.
                pl["text_styles"] = TextStyles.TextStyleDefs();
                pl["dim_styles"] = TextStyles.DimStyleDefs();
            }

            pl["xdata_app"] = "HS-STEEL";
            payloads.Add(pl);
        }

        return Json(new JsonObject
        {
            ["project"] = p.Name,
            ["sheets"] = new JsonArray([.. sheets.Select(s => (JsonNode)new JsonObject { ["dwg_no"] = s.Number, ["title"] = s.Title, ["scale"] = s.Scale, ["entities"] = s.Plan.Entities.Count })]),
            ["total_entities"] = entities.Count,
            ["payload_count"] = payloads.Count,
            ["blocks_needed"] = new JsonArray([.. blocks.Select(b => (JsonNode)b)]),
            ["warnings"] = new JsonArray([.. set.Warnings.Select(w => (JsonNode)w)]),
            ["payloads"] = payloads,
        });
    }

    /// <summary>Adds mark/spec/length/assembly (+ sheet) to the plan tags from the resolved model.</summary>
    private sealed class TagEnricher
    {
        private readonly Dictionary<string, ShapePart> shapes;
        private readonly Dictionary<string, PlatePart> plates;
        private readonly Dictionary<string, Assembly> assemblies;
        private readonly Dictionary<string, string> assemblyOfPart = [];

        public TagEnricher(ModelResult m)
        {
            shapes = m.ShapeParts.GroupBy(x => x.Mark).ToDictionary(g => g.Key, g => g.First());
            plates = m.PlateParts.GroupBy(x => x.Mark).ToDictionary(g => g.Key, g => g.First());
            assemblies = m.Assemblies.GroupBy(x => x.Mark).ToDictionary(g => g.Key, g => g.First());
            foreach (var a in m.Assemblies)
            {
                assemblyOfPart.TryAdd(a.Main.Mark, a.Mark);
                foreach (var at in a.Attachments)
                {
                    assemblyOfPart.TryAdd(at.Part.Mark, a.Mark);
                }
            }
        }

        public JsonObject Enrich(JsonObject? tag, string sheet)
        {
            var t = tag?.DeepClone().AsObject() ?? [];
            t["sheet"] = sheet;
            var mark = t["mark"]?.GetValue<string>();
            if (mark is null)
            {
                return t;
            }

            var kind = t["kind"]?.GetValue<string>();
            if ((kind is "assembly" or "member" or null) && assemblies.TryGetValue(mark, out var a))
            {
                t["spec"] = a.Main.Profile.Spec;
                t["length"] = Math.Round(a.Main.Length, 1);
                t["assembly"] = a.Mark;
            }
            else if (shapes.TryGetValue(mark, out var s))
            {
                t["spec"] = s.Profile.Spec;
                t["length"] = Math.Round(s.Length, 1);
                if (assemblyOfPart.TryGetValue(mark, out var am))
                {
                    t["assembly"] = am;
                }
            }
            else if (plates.TryGetValue(mark, out var pl))
            {
                t["spec"] = pl.Name;
                t["length"] = Math.Round(pl.SizeU, 1);
                if (assemblyOfPart.TryGetValue(mark, out var am))
                {
                    t["assembly"] = am;
                }
            }

            return t;
        }
    }
}

[McpServerResourceType]
public sealed class AssetResources(AssetOptions opt)
{
    [McpServerResource(UriTemplate = "hs://assets/stats", Name = "hs_assets_stats", MimeType = "application/json")]
    [Description("Row counts of the HS-STEEL asset knowledge base by table, nodes by kind, and DB build meta.")]
    public string Stats()
    {
        using var cn = AssetDb.Open(opt);
        var o = new JsonObject { ["db"] = Path.GetFullPath(opt.DbPath) };
        var tables = new JsonObject();
        foreach (var t in new[] { "source_file", "section", "bolt", "section_alias", "block", "palette_item", "command", "command_alias", "linetype", "doc_chunk", "node", "edge" })
        {
            tables[t] = Convert.ToInt64(AssetDb.Rows(cn, $"SELECT COUNT(*) FROM {t}")[0][0], CultureInfo.InvariantCulture);
        }

        o["tables"] = tables;
        var kinds = new JsonObject();
        foreach (var r in AssetDb.Rows(cn, "SELECT kind, COUNT(*) FROM node GROUP BY kind ORDER BY kind"))
        {
            kinds[(string)r[0]!] = Convert.ToInt64(r[1], CultureInfo.InvariantCulture);
        }

        o["nodes_by_kind"] = kinds;
        var meta = new JsonObject();
        foreach (var r in AssetDb.Rows(cn, "SELECT key, value FROM meta ORDER BY key"))
        {
            meta[(string)r[0]!] = (string)r[1]!;
        }

        o["meta"] = meta;
        return o.ToJsonString();
    }

    [McpServerResource(UriTemplate = "hs://sections/{family}", Name = "hs_sections_family", MimeType = "application/json")]
    [Description("All sections of a family (e.g. H-BEAM, ANGLE) with dimensions and unit weight.")]
    public string Sections(string family)
    {
        using var cn = AssetDb.Open(opt);
        var rows = AssetDb.Rows(cn, "SELECT key, spec, shape, m2, m3, m4, m5, m6, m7, unit_weight, paint_area FROM section WHERE family = $f COLLATE NOCASE ORDER BY id", ("$f", Uri.UnescapeDataString(family)));
        return new JsonObject
        {
            ["family"] = family,
            ["count"] = rows.Count,
            ["sections"] = new JsonArray([.. rows.Select(r => (JsonNode)new JsonObject
            {
                ["key"] = (string)r[0]!, ["spec"] = (string)r[1]!, ["shape"] = (string)r[2]!,
                ["dims"] = new JsonArray([.. r.Skip(3).Take(6).Select(v => v is null ? null : (JsonNode)Convert.ToDouble(v, CultureInfo.InvariantCulture))]),
                ["kg_per_m"] = Convert.ToDouble(r[9], CultureInfo.InvariantCulture),
                ["paint_m2_per_m"] = r[10] is null ? null : Convert.ToDouble(r[10], CultureInfo.InvariantCulture),
            })]),
        }.ToJsonString();
    }
}
