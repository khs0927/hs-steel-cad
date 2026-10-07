using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HsSteel.Knowledge.Rules;

public sealed record RuleEvidence(string Source, string Note, string Locator = "");

/// <summary>How a rule was checked: method (numeric-fit, cross-source, identity-check, recompute, geometry, code-read, doc, none), result (pass|partial|fail|unverified).</summary>
public sealed record RuleVerification(string Method, string Result, string Detail);

public sealed record SteelRule(
    string Id,
    string Title,
    string Statement,
    string Category,
    IReadOnlyList<RuleEvidence> Evidence,
    IReadOnlyList<string> EngineRefs,
    IReadOnlyList<string> Tags,
    JsonElement? Value = null,
    string Trust = "stated",
    JsonElement? Formula = null,
    string? Rationale = null,
    string? InferredBy = null,
    RuleVerification? Verification = null,
    string EngineStatus = "n/a",
    string? EngineNote = null,
    IReadOnlyList<string>? Governs = null)
{
    /// <summary>verified = a numeric/cross-source check passed; stated = single source as written; inferred = model interpretation (see Rationale).</summary>
    public static readonly IReadOnlyList<string> TrustLevels = ["verified", "stated", "inferred"];
}

public sealed class RulesCatalog
{
    private static readonly Lazy<RulesCatalog> Lazy = new(LoadEmbeddedOrDisk);
    private readonly Dictionary<string, SteelRule> byId;
    private readonly IReadOnlyList<SteelRule> all;

    public RulesCatalog(IReadOnlyList<SteelRule> rules)
    {
        all = rules;
        byId = rules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static RulesCatalog Instance => Lazy.Value;

    public int Count => all.Count;

    public IReadOnlyList<SteelRule> All => all;

    public SteelRule? Get(string id) => byId.TryGetValue(id, out var r) ? r : null;

    /// <summary>Match rules whose id/title/statement/tags/category contain any query token (case-insensitive).</summary>
    public IReadOnlyList<SteelRule> Search(string query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return [];
        }

        var tokens = query.Split([' ', '\t', '\r', '\n', ',', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant()).Where(t => t.Length >= 2).Distinct().ToList();
        if (tokens.Count == 0)
        {
            tokens = [query.Trim().ToLowerInvariant()];
        }

        return all
            .Select(r => (Rule: r, Score: Score(r, tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Rule.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(x => x.Rule)
            .ToList();
    }

    public IReadOnlyList<SteelRule> ByCategory(string category) =>
        all.Where(r => r.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<SteelRule> ByTag(string tag) =>
        all.Where(r => r.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase))).ToList();

    public IReadOnlyList<SteelRule> CrossCheckEngine(string pathFragment) =>
        all.Where(r => r.EngineRefs.Any(e => e.Contains(pathFragment, StringComparison.OrdinalIgnoreCase))).ToList();

    private static int Score(SteelRule r, List<string> tokens)
    {
        var hay = string.Join('\n', [r.Id, r.Title, r.Statement, r.Category, .. r.Tags, .. r.EngineRefs]).ToLowerInvariant();
        var s = 0;
        foreach (var t in tokens)
        {
            if (r.Id.Equals(t, StringComparison.OrdinalIgnoreCase))
            {
                s += 10;
            }
            else if (r.Tags.Any(x => x.Equals(t, StringComparison.OrdinalIgnoreCase)))
            {
                s += 5;
            }
            else if (hay.Contains(t, StringComparison.Ordinal))
            {
                s += 1;
            }
        }

        return s;
    }

    private static RulesCatalog LoadEmbeddedOrDisk()
    {
        var json = TryReadEmbedded() ?? TryReadBesideAssembly() ?? TryReadRepo()
            ?? throw new InvalidOperationException("rules.json not found (embed, beside assembly, or src/HsSteel.Knowledge/Rules/).");
        return FromJson(json);
    }

    public static RulesCatalog FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("rules");
        var list = new List<SteelRule>();
        foreach (var el in arr.EnumerateArray())
        {
            var evidence = el.GetProperty("evidence").EnumerateArray()
                .Select(e => new RuleEvidence(e.GetProperty("source").GetString()!, e.GetProperty("note").GetString() ?? "",
                    e.TryGetProperty("locator", out var loc) ? loc.GetString() ?? "" : ""))
                .ToList();
            var engine = el.TryGetProperty("engine_refs", out var er)
                ? er.EnumerateArray().Select(x => x.GetString()!).ToList()
                : [];
            var tags = el.TryGetProperty("tags", out var tg)
                ? tg.EnumerateArray().Select(x => x.GetString()!).ToList()
                : [];
            JsonElement? value = el.TryGetProperty("value", out var v) ? v.Clone() : null;
            JsonElement? formula = el.TryGetProperty("formula", out var f) ? f.Clone() : null;
            RuleVerification? ver = el.TryGetProperty("verification", out var vr)
                ? new RuleVerification(Str(vr, "method") ?? "none", Str(vr, "result") ?? "unverified", Str(vr, "detail") ?? "")
                : null;
            var governs = el.TryGetProperty("governs", out var gv) ? gv.EnumerateArray().Select(x => x.GetString()!).ToList() : null;
            list.Add(new SteelRule(
                el.GetProperty("id").GetString()!,
                el.GetProperty("title").GetString()!,
                el.GetProperty("statement").GetString()!,
                el.GetProperty("category").GetString()!,
                evidence, engine, tags, value,
                Str(el, "trust") ?? "stated", formula, Str(el, "rationale"), Str(el, "inferred_by"), ver,
                Str(el, "engine_status") ?? "n/a", Str(el, "engine_note"), governs));
        }

        return new RulesCatalog(new ReadOnlyCollection<SteelRule>(list));
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    public IReadOnlyList<SteelRule> ByTrust(string trust) =>
        all.Where(r => r.Trust.Equals(trust, StringComparison.OrdinalIgnoreCase)).ToList();

    private static string? TryReadEmbedded()
    {
        var asm = typeof(RulesCatalog).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("rules.json", StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return null;
        }

        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private static string? TryReadBesideAssembly()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "Rules", "rules.json");
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }

    private static string? TryReadRepo()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "src", "HsSteel.Knowledge", "Rules", "rules.json");
            if (File.Exists(p))
            {
                return File.ReadAllText(p);
            }
        }

        return null;
    }
}
