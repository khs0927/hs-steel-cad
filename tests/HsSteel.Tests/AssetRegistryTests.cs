using System.Text.Json;
using System.Text.Json.Nodes;
using HsSteel.AssetRegistry;
using Json.Schema;

namespace HsSteel.Tests;

/// <summary>
/// assets/registry/asset-registry.json: valid against its JSON Schema, in sync with tools/AssetRegistry, unique ids,
/// and every source path resolvable (repo files on disk; legacy files in assets/manifest.json, and on disk when
/// HS_STEEL_LEGACY is available).
/// </summary>
public class AssetRegistryTests
{
    private static readonly string Repo = AssetRegistryBuilder.FindRepoRoot(AppContext.BaseDirectory);

    private static readonly Lazy<JsonObject> Registry = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(Repo, AssetRegistryBuilder.RegistryRel)))!.AsObject());

    // Built once: JsonSchema.Net registers schemas by $id globally and refuses to register the same $id twice.
    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(File.ReadAllText(Path.Combine(Repo, AssetRegistryBuilder.SchemaRel))));

    private static IEnumerable<JsonObject> Assets => Registry.Value["assets"]!.AsArray().Select(a => a!.AsObject());

    private static string S(JsonNode? n) => n!.GetValue<string>();

    [Fact]
    public void Registry_ValidatesAgainstSchema()
    {
        var schema = Schema.Value;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, AssetRegistryBuilder.RegistryRel)));
        var result = schema.Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = (result.Details ?? []).Where(d => d.Errors is { Count: > 0 })
            .Take(20).Select(d => $"{d.InstanceLocation}: {string.Join("; ", d.Errors!.Values)}");
        Assert.True(result.IsValid, "Schema errors:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void Schema_RejectsABrokenAsset()
    {
        var schema = Schema.Value;
        var broken = Registry.Value.DeepClone().AsObject();
        var first = broken["assets"]!.AsArray()[0]!.AsObject();
        first.Remove("id");
        first["category"] = "not-a-category";
        using var doc = JsonDocument.Parse(broken.ToJsonString());
        Assert.False(schema.Evaluate(doc.RootElement).IsValid);
    }

    [Fact]
    public void Registry_IsInSyncWithGenerator()
    {
        var expected = AssetRegistryBuilder.Serialize(AssetRegistryBuilder.Build(Repo));
        var actual = File.ReadAllText(Path.Combine(Repo, AssetRegistryBuilder.RegistryRel)).ReplaceLineEndings("\n");
        Assert.True(expected == actual, "assets/registry/asset-registry.json is stale. Run: dotnet run --project tools/AssetRegistry");
    }

    [Fact]
    public void Ids_AreUniqueSortedAndPrefixedByCategory()
    {
        var ids = Assets.Select(a => S(a["id"])).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.All(Assets, a => Assert.StartsWith(S(a["category"]) + "/", S(a["id"]), StringComparison.Ordinal));
    }

    [Fact]
    public void Counts_MatchAssetsAndCategories()
    {
        var counts = Registry.Value["counts"]!;
        Assert.Equal(Assets.Count(), counts["total"]!.GetValue<int>());
        var declared = Registry.Value["categories"]!.AsArray().Select(c => S(c!["id"])).ToList();
        Assert.Equal(AssetRegistryBuilder.Categories.Select(c => c.Id), declared);
        foreach (var g in Assets.GroupBy(a => S(a["category"])))
        {
            Assert.Contains(g.Key, declared);
            Assert.Equal(g.Count(), counts["byCategory"]![g.Key]!.GetValue<int>());
        }
    }

    [Fact]
    public void RepoSources_ExistAndContainTheirSymbol()
    {
        var missing = new List<string>();
        foreach (var a in Assets.Where(a => S(a["source"]!["root"]) == "repo"))
        {
            var path = Path.Combine(Repo, S(a["source"]!["path"]).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                missing.Add($"{S(a["id"])}: {path} not found");
                continue;
            }

            var symbol = a["source"]!["symbol"]?.GetValue<string>();
            if (symbol is not null && S(a["format"]) == "csharp" && !File.ReadAllText(path).Contains(symbol.Split('.')[^1], StringComparison.Ordinal))
            {
                missing.Add($"{S(a["id"])}: symbol {symbol} not found in {S(a["source"]!["path"])}");
            }
        }

        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    [Fact]
    public void LegacySources_AreInTheManifestWithSameHash()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, AssetRegistryBuilder.ManifestRel)));
        var files = manifest.RootElement.GetProperty("files").EnumerateArray()
            .Where(f => f.GetProperty("disposition").GetString() != "excluded")
            .ToDictionary(f => f.GetProperty("relPath").GetString()!, f => f.TryGetProperty("sha256", out var h) ? h.GetString() : null, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var a in Assets.Where(a => S(a["source"]!["root"]) == "legacy"))
        {
            var rel = S(a["source"]!["path"]);
            if (S(a["category"]) == "icon-set")
            {
                var members = a["props"]!["members"]!.AsArray().Select(m => rel + "/" + S(m)).ToList();
                problems.AddRange(members.Where(m => !files.ContainsKey(m)).Select(m => $"{S(a["id"])}: member {m} not in manifest"));
                continue;
            }

            if (!files.TryGetValue(rel, out var sha))
            {
                problems.Add($"{S(a["id"])}: {rel} not in manifest");
            }
            else if (sha != a["source"]!["sha256"]?.GetValue<string>())
            {
                problems.Add($"{S(a["id"])}: sha256 differs from manifest");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(30)));

        // Every non-excluded manifest file is represented exactly once (directly or as an icon-set member).
        var covered = Assets.Where(a => S(a["source"]!["root"]) == "legacy")
            .SelectMany(a => S(a["category"]) == "icon-set"
                ? a["props"]!["members"]!.AsArray().Select(m => S(a["source"]!["path"]) + "/" + S(m))
                : [S(a["source"]!["path"])])
            .ToList();
        Assert.Equal(covered.Count, covered.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(files.Keys.Order(StringComparer.Ordinal), covered.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Blocks_AreThe114LegacyBlockDwgs()
    {
        var blocks = Assets.Where(a => S(a["category"]) == "block").ToList();
        Assert.Equal(114, blocks.Count);
        Assert.All(blocks, b => Assert.EndsWith(".dwg", S(b["source"]!["path"]), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blocks, b => S(b["props"]!["blockCategory"]) == "frame");
    }

    [Fact]
    public void CodeAssets_MirrorTheDraftingStandards()
    {
        var ids = Assets.Select(a => S(a["id"])).ToHashSet(StringComparer.Ordinal);
        Assert.All(HsSteel.Drafting.Layers.All, l => Assert.Contains($"layer/{l.Name}", ids));
        Assert.Contains($"text-style/{HsSteel.Drafting.TextStyles.Korean}", ids);
        Assert.Contains($"title-block/{HsSteel.Drafting.SheetFrame.A3Default.BlockName}", ids);
        foreach (var k in new[] { "splice", "shear_tab", "base_plate", "end_cap", "end_plate" })
        {
            Assert.Contains($"connection-detail/{k}", ids);
        }
    }

    [Theory]
    [InlineData("a3-1b", "frame")]
    [InlineData("REV_TITLE", "revision")]
    [InlineData("BOLT-DATA-TABLE", "table")]
    [InlineData("HAS M20x240K", "anchor")]
    [InlineData("LIFTING-LUG-90x90x15T", "lug")]
    [InlineData("제작도_표지_짱구", "cover")]
    [InlineData("ZZZ", "misc")]
    public void BlockCategory_FilenameRules(string name, string expected) => Assert.Equal(expected, AssetRegistryBuilder.BlockCategory(name));

    [LegacyAssetFact]
    public void LegacySources_ExistOnDisk_WhenLegacyTreeIsAvailable()
    {
        var root = Directory.GetParent(Path.GetFullPath(Fx.Legacy).TrimEnd('\\', '/'))!.FullName;
        var missing = Assets.Where(a => S(a["source"]!["root"]) == "legacy")
            .Select(a => Path.Combine(root, S(a["source"]!["path"]).Replace('/', Path.DirectorySeparatorChar)))
            .Where(p => !File.Exists(p) && !Directory.Exists(p))
            .ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} legacy paths missing under {root}:\n" + string.Join("\n", missing.Take(30)));
    }
}
