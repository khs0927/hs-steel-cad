using System.Text.Json;
using HsSteel.Knowledge;

namespace HsSteel.Knowledge.Tests;

/// <summary>K3: offline ONNX query embedder + hybrid search Hit@5 on a fixed ~40-query eval set.</summary>
public sealed class SemanticSearchTests(KnowledgeFixture fx) : IClassFixture<KnowledgeFixture>
{
    private static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "HsSteel.sln")))
            {
                return d.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string ModelDir => Path.Combine(FindRepo(), "out", "knowledge", "model");
    private static string EvalPath => Path.Combine(FindRepo(), "tests", "HsSteel.Knowledge.Tests", "Data", "eval_queries.json");

    [Fact]
    public void QueryEmbedder_MatchesTokenizerRef_CosineHigh()
    {
        if (!File.Exists(Path.Combine(ModelDir, QueryEmbedder.ModelFile)))
        {
            return;
        }

        var e = QueryEmbedder.TryCreate(ModelDir); // process-cached; do not Dispose
        Assert.NotNull(e);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ModelDir, "tokenizer_ref.json")));
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            var text = r.GetProperty("text").GetString()!;
            var expectIds = r.GetProperty("ids").EnumerateArray().Select(x => x.GetInt64()).ToArray();
            var expectVec = r.GetProperty("vec").EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
            var q = text.StartsWith("query: ", StringComparison.Ordinal) ? text["query: ".Length..] : text;
            Assert.Equal(expectIds, e!.Tokenize(text));
            var got = e.Embed(q);
            double dot = 0, n1 = 0, n2 = 0;
            for (var i = 0; i < expectVec.Length; i++)
            {
                dot += got[i] * expectVec[i];
                n1 += got[i] * got[i];
                n2 += expectVec[i] * expectVec[i];
            }

            Assert.True(dot / (Math.Sqrt(n1) * Math.Sqrt(n2)) >= 0.95, text);
        }
    }

    [Fact]
    public void Real_SemanticAvailable_WhenModelAndVectorsPresent()
    {
        if (!fx.RealAvailable || !File.Exists(Path.Combine(ModelDir, QueryEmbedder.ModelFile)))
        {
            return;
        }

        using var s = new KnowledgeStore(fx.RealA);
        Assert.True(s.SemanticAvailable);
        Assert.True(Scalar(fx.RealA, "SELECT COUNT(*) FROM doc_chunk_vec") >= 900);
    }

    [Fact]
    public void Real_HybridSearch_MarksVectorWhenFused()
    {
        if (!fx.RealAvailable || !File.Exists(Path.Combine(ModelDir, QueryEmbedder.ModelFile)))
        {
            return;
        }

        using var s = new KnowledgeStore(fx.RealA);
        var hits = s.Search("프로젝트 시작하기", "doc_chunk", 10, SearchMode.Auto);
        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.MatchedBy.Contains("vector", StringComparison.Ordinal));
    }

    [Fact]
    public void Real_DanjoongChunks_DownWeightedInSemantic()
    {
        if (!fx.RealAvailable || !File.Exists(Path.Combine(ModelDir, QueryEmbedder.ModelFile)))
        {
            return;
        }

        using var s = new KnowledgeStore(fx.RealA);
        var hits = s.Search("축척조정 SCL", "doc_chunk", 10, SearchMode.Semantic);
        Assert.NotEmpty(hits);
        // Number-heavy 단중.xlsx tables must not dominate the top of a how-to query.
        Assert.DoesNotContain(hits.Take(5), h => h.Label.Contains("단중.xlsx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Real_EvalHitAt5_AtLeast80Percent()
    {
        if (!fx.RealAvailable || !File.Exists(Path.Combine(ModelDir, QueryEmbedder.ModelFile)) || !File.Exists(EvalPath))
        {
            return;
        }

        using var s = new KnowledgeStore(fx.RealA);
        Assert.True(s.SemanticAvailable);
        using var doc = JsonDocument.Parse(File.ReadAllText(EvalPath));
        var items = doc.RootElement.EnumerateArray().ToList();
        Assert.True(items.Count >= 35, $"eval set too small: {items.Count}");
        var hits = 0;
        var misses = new List<string>();
        foreach (var item in items)
        {
            var q = item.GetProperty("q").GetString()!;
            var expect = item.GetProperty("expect").EnumerateArray().Select(e => e.GetString()!).ToArray();
            var top = s.Search(q, "doc_chunk", 5, SearchMode.Auto);
            var ok = top.Any(h => expect.Any(ex => h.Label.Contains(ex, StringComparison.OrdinalIgnoreCase)
                || h.Key.Contains(ex, StringComparison.OrdinalIgnoreCase)));
            if (ok)
            {
                hits++;
            }
            else
            {
                misses.Add($"{q} => [{string.Join("; ", top.Select(h => h.Label))}]");
            }
        }

        var rate = hits / (double)items.Count;
        Assert.True(rate >= 0.8, $"Hit@5={rate:F3} ({hits}/{items.Count}); misses:\n" + string.Join("\n", misses));
    }

    private static long Scalar(string db, string sql)
    {
        using var cn = new Microsoft.Data.Sqlite.SqliteConnection(KnowledgeDbBuilder.ConnectionString(db, Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly));
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }
}