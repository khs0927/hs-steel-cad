using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace HsSteel.Knowledge;

/// <summary>Offline multilingual-e5-small query embedder (ONNX + SentencePiece). Files come from tools/docs_chunker/export_onnx.py
/// into out/knowledge/model/. <see cref="TryCreate"/> returns null when the model is missing or fails to load.</summary>
public sealed class QueryEmbedder : IDisposable
{
    public const string ModelFile = "model.onnx";
    public const string TokenizerFile = "sentencepiece.bpe.model";
    public const int Dim = 384;
    private const int MaxTokens = 256;

    private readonly InferenceSession session;
    private readonly SentencePieceTokenizer tokenizer;
    private readonly object gate = new();

    private QueryEmbedder(InferenceSession session, SentencePieceTokenizer tokenizer)
    {
        this.session = session;
        this.tokenizer = tokenizer;
    }

    public static string? FindModelDir(string? dbPath = null)
    {
        var env = Environment.GetEnvironmentVariable("HS_KNOWLEDGE_MODEL_DIR");
        var cands = new List<string>();
        if (!string.IsNullOrEmpty(env))
        {
            cands.Add(env);
        }

        if (dbPath is not null)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (dir is not null)
            {
                cands.Add(Path.Combine(dir, "knowledge", "model"));
            }
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                cands.Add(Path.Combine(d.FullName, "out", "knowledge", "model"));
            }
        }

        return cands.FirstOrDefault(c => File.Exists(Path.Combine(c, ModelFile)) && File.Exists(Path.Combine(c, TokenizerFile)));
    }

    private static readonly Dictionary<string, QueryEmbedder?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Process-wide cached instance per model dir; null if unavailable.</summary>
    public static QueryEmbedder? TryCreate(string? modelDir)
    {
        if (modelDir is null)
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(modelDir, out var cached))
            {
                return cached;
            }

            QueryEmbedder? e = null;
            try
            {
                using var fs = File.OpenRead(Path.Combine(modelDir, TokenizerFile));
                var tok = SentencePieceTokenizer.Create(fs, addBeginningOfSentence: false, addEndOfSentence: false);
                var opts = new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
                e = new QueryEmbedder(new InferenceSession(Path.Combine(modelDir, ModelFile), opts), tok);
            }
            catch (Exception)
            {
                e = null;
            }

            Cache[modelDir] = e;
            return e;
        }
    }

    /// <summary>HF XLM-R ids from a SentencePiece model: &lt;s&gt;=0, &lt;/s&gt;=2, unk=3, other pieces = spm id + 1.</summary>
    public long[] Tokenize(string text)
    {
        var spm = tokenizer.EncodeToIds(text, considerPreTokenization: false, considerNormalization: true);
        var ids = new List<long>(spm.Count + 2) { 0 };
        foreach (var id in spm.Take(MaxTokens - 2))
        {
            ids.Add(id == 0 ? 3 : id + 1);
        }

        ids.Add(2);
        return [.. ids];
    }

    /// <summary>L2-normalized mean-pooled embedding of "query: " + text.</summary>
    public float[] Embed(string query)
    {
        var ids = Tokenize("query: " + query);
        var n = ids.Length;
        var idT = new DenseTensor<long>(ids, [1, n]);
        var maskT = new DenseTensor<long>(Enumerable.Repeat(1L, n).ToArray(), [1, n]);
        float[] outv;
        lock (gate)
        {
            using var res = session.Run([NamedOnnxValue.CreateFromTensor("input_ids", idT), NamedOnnxValue.CreateFromTensor("attention_mask", maskT)]);
            outv = res.First().AsTensor<float>().ToArray();
        }

        var v = new float[Dim];
        for (var t = 0; t < n; t++)
        {
            for (var d = 0; d < Dim; d++)
            {
                v[d] += outv[t * Dim + d];
            }
        }

        double norm = 0;
        for (var d = 0; d < Dim; d++)
        {
            v[d] /= n;
            norm += (double)v[d] * v[d];
        }

        var inv = norm > 0 ? (float)(1.0 / Math.Sqrt(norm)) : 0f;
        for (var d = 0; d < Dim; d++)
        {
            v[d] *= inv;
        }

        return v;
    }

    public void Dispose() => session.Dispose();
}
