using System.Text.Json;
using HsSteel.Knowledge;
using HsSteel.Knowledge.Rules;

namespace HsSteel.Knowledge.Tests;

public class GraphRagTests
{
    private static string? FindDb()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
            {
                var c = Path.Combine(d.FullName, "out", "hs_assets.db");
                if (File.Exists(c))
                {
                    return c;
                }
            }
        }

        return null;
    }

    private static string FindData(string name)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
            {
                var c = Path.Combine(d.FullName, "tests", "HsSteel.Knowledge.Tests", "Data", name);
                if (File.Exists(c))
                {
                    return c;
                }
            }
        }

        throw new FileNotFoundException(name);
    }

    [Fact]
    public void Rules_catalog_has_at_least_80_evidence_backed_rules()
    {
        Assert.True(RulesCatalog.Instance.Count >= 80, $"catalog size {RulesCatalog.Instance.Count}");
        var withEvidence = RulesCatalog.Instance.All.Count(r => r.Evidence.Count > 0);
        Assert.Equal(RulesCatalog.Instance.Count, withEvidence);
        var withEngine = RulesCatalog.Instance.All.Count(r => r.EngineRefs.Count > 0);
        Assert.True(withEngine >= 40, $"engine-cross-checked rules {withEngine}");
    }

    [Fact]
    public void Rules_search_finds_scallop_and_fillet()
    {
        var scallop = RulesCatalog.Instance.Search("scallop SCALLOP");
        Assert.Contains(scallop, r => r.Id is "PD-002" or "DR-001");
        var fillet = RulesCatalog.Instance.Search("fillet weld KDS");
        Assert.True(fillet.Count >= 1);
    }

    [Fact]
    public void Graph_rag_and_explain_smoke()
    {
        var db = FindDb();
        Assert.False(string.IsNullOrEmpty(db), "out/hs_assets.db missing");
        using var store = new KnowledgeStore(db!);
        var rag = GraphRag.Query(store, "H400x200 shear tab scallop", searchLimit: 6, expandDepth: 1, mode: SearchMode.Lexical);
        Assert.NotEmpty(rag.ContextText);
        Assert.True(rag.Items.Count >= 1 || rag.Rules.Count >= 1);

        // Prefer a known section if present
        var sec = store.Search("H400x200", "section", 3, SearchMode.Lexical).FirstOrDefault();
        if (sec is not null)
        {
            var ex = HsExplain.Explain(store, sec.Kind, sec.Key, depth: 1);
            Assert.Equal(sec.Key, ex.Node.Key);
            Assert.False(string.IsNullOrWhiteSpace(ex.Narrative));
        }
    }

    [Fact]
    public void Graph_rag_eval_hit_rate()
    {
        var db = FindDb();
        Assert.False(string.IsNullOrEmpty(db), "out/hs_assets.db missing");
        using var store = new KnowledgeStore(db!);
        var path = FindData("graph_rag_eval.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var qs = doc.RootElement.GetProperty("questions").EnumerateArray().ToList();
        var pass = 0;
        var details = new List<object>();
        foreach (var q in qs)
        {
            var id = q.GetProperty("id").GetString()!;
            var query = q.GetProperty("q").GetString()!;
            var expectRules = q.GetProperty("expect_rules").EnumerateArray().Select(x => x.GetString()!).ToList();
            var anyRules = q.TryGetProperty("expect_any_rules", out var ar) && ar.GetBoolean();
            var expectKinds = q.TryGetProperty("expect_kinds", out var ek)
                ? ek.EnumerateArray().Select(x => x.GetString()!).Where(s => s!.Length > 0).ToList()
                : [];
            var expectKeys = q.TryGetProperty("expect_keys", out var ekeys)
                ? ekeys.EnumerateArray().Select(x => x.GetString()!).ToList()
                : [];

            var rag = GraphRag.Query(store, query, searchLimit: 10, expandDepth: 1, ruleLimit: 12, mode: SearchMode.Auto);
            var ruleIds = rag.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Also allow catalog-only match for rule questions (even if search empty)
            foreach (var r in RulesCatalog.Instance.Search(query, 12))
            {
                ruleIds.Add(r.Id);
            }

            var ruleHit = anyRules
                ? expectRules.Any(ruleIds.Contains)
                : expectRules.Count == 0 || expectRules.Any(ruleIds.Contains);
            var kindHit = expectKinds.Count == 0
                || rag.Items.Any(i => expectKinds.Contains(i.Kind, StringComparer.OrdinalIgnoreCase));
            var keyHit = expectKeys.Count == 0
                || rag.Items.Any(i => expectKeys.Any(k => i.Key.Contains(k, StringComparison.OrdinalIgnoreCase)
                    || i.Label.Contains(k, StringComparison.OrdinalIgnoreCase)));
            var ok = ruleHit && kindHit && keyHit;
            if (ok)
            {
                pass++;
            }

            details.Add(new { id, ok, ruleHit, kindHit, keyHit, got_rules = ruleIds.Order().Take(8).ToList(), seeds = rag.Items.Where(i => i.Role == "seed").Select(i => $"{i.Kind}/{i.Key}").Take(5).ToList() });
        }

        var rate = (double)pass / qs.Count;
        var reportPath = Path.Combine(Path.GetDirectoryName(FindDb())!, "graph_rag_eval_results.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { pass, total = qs.Count, hit_at_rule = rate, details }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(rate >= 0.70, $"Graph-RAG eval Hit@rules {rate:P1} ({pass}/{qs.Count}); see {reportPath}");
    }
}
