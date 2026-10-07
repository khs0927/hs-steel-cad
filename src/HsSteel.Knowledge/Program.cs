using HsSteel.Knowledge;

// Usage:
//   dotnet run --project src/HsSteel.Knowledge -- manifest [--root C:\HS-STEEL] [--out assets/manifest.json]
//   dotnet run --project src/HsSteel.Knowledge -- build-db [--root C:\HS-STEEL] [--manifest assets/manifest.json] [--out out/hs_assets.db]
//   dotnet run --project src/HsSteel.Knowledge -- search <query> [--db out/hs_assets.db] [--kind section] [--limit 20] [--mode auto|lexical|semantic]
var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var positional = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
    {
        opts[args[i][2..]] = args[++i];
    }
    else
    {
        positional.Add(args[i]);
    }
}

var repo = FindRepoRoot();
string Opt(string k, string d) => opts.TryGetValue(k, out var v) ? v : d;
var root = Opt("root", ManifestScanner.DefaultRoot());

switch (positional.FirstOrDefault())
{
    case "manifest":
    {
        var outPath = Opt("out", Path.Combine(repo, "assets", "manifest.json"));
        var m = ManifestScanner.Scan(root);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        m.Save(outPath);
        Console.WriteLine($"{m.FileCount} files -> {outPath} ({string.Join(", ", m.Counts.Select(c => $"{c.Key}={c.Value}"))})");
        return m.Counts.ContainsKey(ManifestScanner.Unknown) ? 2 : 0;
    }

    case "build-db":
    {
        var manPath = Opt("manifest", Path.Combine(repo, "assets", "manifest.json"));
        var m = File.Exists(manPath) ? Manifest.Load(manPath) : ManifestScanner.Scan(root);
        var outPath = Opt("out", Path.Combine(repo, "out", "hs_assets.db"));
        var b = new KnowledgeDbBuilder(m).IngestSections(root);
        var blockDir = Path.Combine(root, "HSSTEEL", "block");
        var supportDir = Path.Combine(root, "HSSTEEL", "support");
        var blocks = AssetAdapters.Blocks(root, blockDir, previews: !opts.ContainsKey("no-previews"));
        b.IngestBlocks(blocks);
        var sup = AssetAdapters.Support(supportDir, "HSSTEEL/support");
        b.IngestPaletteItems(sup.PaletteItems).IngestCommands(sup.Commands).IngestCommandAliases(sup.Aliases)
            .IngestLinetypes(sup.Linetypes).IngestMlineStyles(sup.MlineStyles).IngestFontMaps(sup.FontMaps);
        var chunksPath = Opt("chunks", Path.Combine(repo, "out", "knowledge", "chunks.jsonl"));
        var chunks = AssetAdapters.DocChunks(chunksPath);
        b.IngestDocChunks(chunks);
        var vecs = KnowledgeDbBuilder.LoadEmbeddingFiles(Path.GetDirectoryName(Path.GetFullPath(chunksPath))!);
        b.IngestEmbeddings(vecs);
        b.Build(outPath);
        var rebornRoot = Opt("reborn", HsSteel.Knowledge.Reborn.RebornManifest.DefaultRoot);
        if (Directory.Exists(rebornRoot)) Console.WriteLine(HsSteel.Knowledge.Reborn.RebornIngest.Apply(outPath, rebornRoot).Summary);
        Console.WriteLine(HsSteel.Knowledge.Ingest.AssetIngest.Apply(outPath, m, root, Path.Combine(repo, "out", "knowledge", "coverage.json")).Summary);
        Console.WriteLine(HsSteel.Knowledge.Rules.RulesIngest.Apply(outPath).Summary);
        Console.WriteLine($"built {outPath}: blocks={blocks.Count} palette_items={sup.PaletteItems.Count} commands={sup.Commands.Count} aliases={sup.Aliases.Count} " +
            $"linetypes={sup.Linetypes.Count} mline_styles={sup.MlineStyles.Count} font_maps={sup.FontMaps.Count} doc_chunks={chunks.Count}" +
            $" doc_chunk_vec={vecs.Count}" + (chunks.Count == 0 ? $" (no chunks at {chunksPath})" : string.Empty));
        foreach (var w in sup.Warnings)
        {
            Console.Error.WriteLine("warn: " + w);
        }

        return 0;
    }

    case "reborn-manifest":
    {
        var rm = HsSteel.Knowledge.Reborn.RebornManifest.Scan(Opt("reborn", HsSteel.Knowledge.Reborn.RebornManifest.DefaultRoot));
        rm.Save(Opt("out", Path.Combine(repo, "assets", "reborn_manifest.json")));
        Console.WriteLine($"{rm.FileCount} files ({string.Join(", ", rm.Counts.Select(c => $"{c.Key}={c.Value}"))})");
        return 0;
    }

    case "search" when positional.Count > 1:
    {
        using var store = new KnowledgeStore(Opt("db", Path.Combine(repo, "out", "hs_assets.db")));
        foreach (var h in store.Search(string.Join(' ', positional.Skip(1)), opts.GetValueOrDefault("kind"), int.Parse(Opt("limit", "20"), System.Globalization.CultureInfo.InvariantCulture),
            Enum.Parse<SearchMode>(Opt("mode", "auto"), true)))
        {
            Console.WriteLine($"{h.Score,8:F2}  {h.MatchedBy,-7} {h.Kind,-10} {h.Key}");
        }

        return 0;
    }

    default:
        Console.Error.WriteLine("usage: manifest | build-db | search <query>  [--root DIR] [--out PATH] [--manifest PATH] [--db PATH] [--kind K] [--limit N]");
        return 1;
}

static string FindRepoRoot()
{
    for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
    {
        if (File.Exists(Path.Combine(d.FullName, "HsSteel.sln")))
        {
            return d.FullName;
        }
    }

    return Directory.GetCurrentDirectory();
}
