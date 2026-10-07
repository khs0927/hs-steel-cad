using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HsSteel.Knowledge.Rules;

namespace HsSteel.Knowledge;

/// <summary>One item in a Graph-RAG context pack (search hit, neighbor, or rule).</summary>
public sealed record GraphRagItem(
    string Role,
    string Kind,
    string Key,
    string Label,
    double Score,
    string? Rel = null,
    int Depth = 0,
    string? Evidence = null,
    string? Text = null);

/// <summary>Hybrid search + multi-hop graph expansion + rules catalog for grounded answers.</summary>
public static class GraphRag
{
    public sealed record Result(
        string Query,
        IReadOnlyList<GraphRagItem> Items,
        IReadOnlyList<SteelRule> Rules,
        string ContextText,
        JsonObject Debug);

    public static Result Query(
        KnowledgeStore store,
        string query,
        string? kind = null,
        int searchLimit = 8,
        int expandDepth = 1,
        int neighborCap = 24,
        int ruleLimit = 8,
        SearchMode mode = SearchMode.Auto)
    {
        var hits = store.Search(query, kind, searchLimit, mode);
        var items = new List<GraphRagItem>();
        var seen = new HashSet<(string, string)>(hits.Select(h => (h.Kind, h.Key)));
        foreach (var h in hits)
        {
            items.Add(new GraphRagItem("seed", h.Kind, h.Key, h.Label, h.Score, Evidence: h.MatchedBy));
            var node = store.Get(h.Kind, h.Key);
            if (node is null)
            {
                continue;
            }

            foreach (var n in store.Neighbors(node.Id, null, Math.Clamp(expandDepth, 0, 3)).Take(neighborCap))
            {
                var key = (n.Node.Kind, n.Node.Key);
                if (!seen.Add(key))
                {
                    continue;
                }

                items.Add(new GraphRagItem(
                    "neighbor", n.Node.Kind, n.Node.Key, n.Node.Label,
                    h.Score / (1 + n.Depth), n.Edge.Rel, n.Depth,
                    Evidence: n.Edge.EvidenceNote ?? n.Edge.EvidenceSourceFile));
            }
        }

        var rules = RulesCatalog.Instance.Search(query, ruleLimit).ToList();
        // Also pull rules tagged by top seed kinds/keys.
        foreach (var h in hits.Take(3))
        {
            foreach (var extra in RulesCatalog.Instance.Search($"{h.Kind} {h.Key} {h.Label}", 3))
            {
                if (rules.All(r => r.Id != extra.Id))
                {
                    rules.Add(extra);
                }
            }
        }

        rules = rules.Take(ruleLimit).ToList();
        foreach (var r in rules)
        {
            items.Add(new GraphRagItem("rule", "rule", r.Id, r.Title, 1.0, Evidence: r.Category, Text: r.Statement));
        }

        var ctx = BuildContext(query, items, rules);
        var dbg = new JsonObject
        {
            ["search_hits"] = hits.Count,
            ["expanded"] = items.Count(i => i.Role == "neighbor"),
            ["rules"] = rules.Count,
            ["mode"] = mode.ToString().ToLowerInvariant(),
            ["semantic_available"] = store.SemanticAvailable,
        };
        return new Result(query, items, rules, ctx, dbg);
    }

    public static JsonObject ToJson(Result r) => new()
    {
        ["query"] = r.Query,
        ["context"] = r.ContextText,
        ["items"] = new JsonArray([.. r.Items.Select(i => (JsonNode)new JsonObject
        {
            ["role"] = i.Role,
            ["kind"] = i.Kind,
            ["key"] = i.Key,
            ["label"] = i.Label,
            ["score"] = i.Score,
            ["rel"] = i.Rel,
            ["depth"] = i.Depth,
            ["evidence"] = i.Evidence,
            ["text"] = i.Text,
        })]),
        ["rules"] = new JsonArray([.. r.Rules.Select(x => (JsonNode)new JsonObject
        {
            ["id"] = x.Id,
            ["title"] = x.Title,
            ["statement"] = x.Statement,
            ["category"] = x.Category,
            ["engine_refs"] = new JsonArray([.. x.EngineRefs]),
            ["evidence"] = new JsonArray([.. x.Evidence.Select(e => (JsonNode)new JsonObject { ["source"] = e.Source, ["note"] = e.Note })]),
        })]),
        ["debug"] = r.Debug,
    };

    private static string BuildContext(string query, List<GraphRagItem> items, List<SteelRule> rules)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Graph-RAG context for: {query}");
        sb.AppendLine();
        sb.AppendLine("## Seeds & neighbors");
        foreach (var i in items.Where(x => x.Role != "rule").Take(40))
        {
            sb.Append("- [").Append(i.Role).Append("] ").Append(i.Kind).Append('/').Append(i.Key);
            sb.Append(" — ").Append(i.Label);
            if (!string.IsNullOrEmpty(i.Rel))
            {
                sb.Append(" (").Append(i.Rel).Append(" d=").Append(i.Depth).Append(')');
            }

            sb.AppendLine();
        }

        if (rules.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Rules");
            foreach (var r in rules)
            {
                sb.Append("- ").Append(r.Id).Append(": ").Append(r.Statement);
                if (r.EngineRefs.Count > 0)
                {
                    sb.Append(" [").Append(string.Join("; ", r.EngineRefs)).Append(']');
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}

/// <summary>Explain a knowledge-graph node with neighbors, related rules, and optional search crumbs.</summary>
public static class HsExplain
{
    public sealed record Result(GraphNode Node, IReadOnlyList<NeighborHit> Neighbors, IReadOnlyList<SteelRule> Rules, string Narrative, JsonObject Json);

    public static Result Explain(KnowledgeStore store, string kind, string key, int depth = 2, int ruleLimit = 10)
    {
        var node = store.Get(kind, key) ?? throw new ArgumentException($"No {kind}/{key} in asset DB.");
        var neigh = store.Neighbors(node.Id, null, Math.Clamp(depth, 1, 4));
        var q = $"{kind} {key} {node.Label}";
        var rules = RulesCatalog.Instance.Search(q, ruleLimit).ToList();
        // Category heuristics
        if (kind is "section" or "family")
        {
            rules = rules.Concat(RulesCatalog.Instance.ByTag("family").Take(3))
                .Concat(RulesCatalog.Instance.ByTag("scss").Take(3))
                .DistinctBy(r => r.Id).Take(ruleLimit).ToList();
        }
        else if (kind is "bolt")
        {
            rules = rules.Concat(RulesCatalog.Instance.ByCategory("bolting").Take(5))
                .DistinctBy(r => r.Id).Take(ruleLimit).ToList();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{node.Kind}/{node.Key} — {node.Label}");
        if (!string.IsNullOrWhiteSpace(node.PropsJson) && node.PropsJson != "{}")
        {
            sb.AppendLine($"props: {Truncate(node.PropsJson, 400)}");
        }

        sb.AppendLine($"neighbors ({neigh.Count}):");
        foreach (var g in neigh.GroupBy(n => n.Edge.Rel).OrderBy(g => g.Key))
        {
            sb.Append("  ").Append(g.Key).Append(": ");
            sb.AppendLine(string.Join(", ", g.Take(12).Select(n => $"{n.Node.Kind}/{n.Node.Key}")));
        }

        if (rules.Count > 0)
        {
            sb.AppendLine("related rules:");
            foreach (var r in rules)
            {
                sb.Append("  ").Append(r.Id).Append(": ").AppendLine(r.Statement);
            }
        }

        var json = new JsonObject
        {
            ["node"] = new JsonObject
            {
                ["kind"] = node.Kind,
                ["key"] = node.Key,
                ["label"] = node.Label,
                ["props_json"] = node.PropsJson,
            },
            ["neighbors"] = new JsonArray([.. neigh.Select(h => (JsonNode)new JsonObject
            {
                ["kind"] = h.Node.Kind,
                ["key"] = h.Node.Key,
                ["label"] = h.Node.Label,
                ["rel"] = h.Edge.Rel,
                ["depth"] = h.Depth,
                ["evidence_source_file"] = h.Edge.EvidenceSourceFile,
                ["evidence_note"] = h.Edge.EvidenceNote,
            })]),
            ["rules"] = new JsonArray([.. rules.Select(r => (JsonNode)new JsonObject
            {
                ["id"] = r.Id,
                ["title"] = r.Title,
                ["statement"] = r.Statement,
                ["category"] = r.Category,
                ["engine_refs"] = new JsonArray([.. r.EngineRefs]),
            })]),
            ["narrative"] = sb.ToString(),
        };
        return new Result(node, neigh, rules, sb.ToString(), json);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
