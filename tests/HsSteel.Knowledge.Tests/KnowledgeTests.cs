using HsSteel.Knowledge;
using Microsoft.Data.Sqlite;

namespace HsSteel.Knowledge.Tests;

/// <summary>Builds the DB once from the real legacy tree (C:\HS-STEEL or parent of HS_STEEL_LEGACY) plus synthetic block/doc records.</summary>
public sealed class KnowledgeFixture : IDisposable
{
    public string Root { get; } = ManifestScanner.DefaultRoot();
    public bool Available => Directory.Exists(Root);
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "hs-knowledge-tests-" + Guid.NewGuid().ToString("N"));
    public Manifest? Manifest { get; }
    public string DbA => Path.Combine(Dir, "a", "hs_assets.db");
    public string DbB => Path.Combine(Dir, "b", "hs_assets.db");
    public string RealA => Path.Combine(Dir, "ra", "hs_assets.db");
    public string RealB => Path.Combine(Dir, "rb", "hs_assets.db");
    public bool RealAvailable { get; private set; }
    public int DocChunkCount { get; private set; }

    public KnowledgeFixture()
    {
        if (!Available)
        {
            return;
        }

        Manifest = ManifestScanner.Scan(Root);
        Build(DbA);
        Build(DbB);
        var blockDir = Path.Combine(Root, "HSSTEEL", "block");
        var supportDir = Path.Combine(Root, "HSSTEEL", "support");
        if (!Directory.Exists(blockDir) || !Directory.Exists(supportDir))
        {
            return;
        }

        RealAvailable = true;
        var blocks = AssetAdapters.Blocks(Root, blockDir);
        var sup = AssetAdapters.Support(supportDir, "HSSTEEL/support");
        var chunks = new List<DocChunkRecord>(AssetAdapters.DocChunks(Path.Combine(FindRepo(), "out", "knowledge", "chunks.jsonl")));
        DocChunkCount = chunks.Count;
        foreach (var path in new[] { RealA, RealB })
        {
            new KnowledgeDbBuilder(Manifest)
                .IngestSections(Root).IngestBlocks(blocks).IngestPaletteItems(sup.PaletteItems).IngestCommands(sup.Commands)
                .IngestCommandAliases(sup.Aliases).IngestLinetypes(sup.Linetypes).IngestMlineStyles(sup.MlineStyles).IngestFontMaps(sup.FontMaps)
                .IngestDocChunks(chunks).Build(path);
        }
    }

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

    public void Build(string path) =>
        new KnowledgeDbBuilder(Manifest!)
            .IngestSections(Root)
            .IngestBlocks([new BlockRecord("HTB-M20", "HSSTEEL/block/none.dwg", 0, 0, 0, -10, -10, 10, 10, 3, null,
                [new BlockAttributeRecord("SIZE", "볼트 규격", "M20")], ["HS-BOLT", "0"])])
            .IngestCommands([new CommandRecord("HSBEAM", "H형강 보 작도", "^C^CHSBEAM", "HSSTEEL/hs02.VLX")])
            .IngestCommandAliases([new CommandAliasRecord("HB", "HSBEAM", "HSSTEEL/support/acad.pgp")])
            .IngestPaletteItems([new PaletteItemRecord("2형강", "보", "command", "HSBEAM", "HSSTEEL/support/x.atc")])
            .IngestDocChunks([new DocChunkRecord("manual-p3-1", "HSSTEEL/도움말/manual.pdf", 3, "에이치빔 보를 배치할 때 고장력볼트 접합 상세를 선택한다.")])
            .Build(path);

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }
}

public sealed class KnowledgeTests(KnowledgeFixture fx) : IClassFixture<KnowledgeFixture>
{
    [Fact]
    public void Manifest_RowCountMatchesDisk_AllClassified()
    {
        if (!fx.Available) return;
        var onDisk = Directory.EnumerateFiles(fx.Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }).Count();
        var m = fx.Manifest!;
        Assert.Equal(onDisk, m.Files.Count);
        Assert.Equal(onDisk, m.FileCount);
        Assert.All(m.Files, f => Assert.Contains(f.Disposition, new[] { "ingest", "reference", "excluded" }));
        Assert.Equal(m.Files.Count, m.Files.Select(f => f.RelPath).Distinct(StringComparer.Ordinal).Count());
        Assert.All(m.Files, f => Assert.Equal(64, f.Sha256.Length));
    }

    [Theory]
    [InlineData("HsRockey/HsRockey.txt", "excluded")]
    [InlineData("HSSTEEL/CSROCKEY2013.dll", "excluded")]
    [InlineData("HSSTEEL/r4nd_class.dll", "excluded")]
    [InlineData("HSSTEEL/block/x.bak", "excluded")]
    [InlineData("HSSTEEL/Icons/Thumbs.db", "excluded")]
    [InlineData("HSSTEEL/plot.log", "excluded")]
    [InlineData("HSSTEEL/새공사-대아/a.M35", "reference")]
    [InlineData("HSSTEEL/새공사-성우/a.dat", "reference")]
    [InlineData("HSSTEEL/attributes/H-BEAM.dat", "ingest")]
    [InlineData("HSSTEEL/block/HTB.dwg", "ingest")]
    public void Classify_LedgerRules(string rel, string disposition) =>
        Assert.Equal(disposition, ManifestScanner.Classify(rel).Disposition);

    [Fact]
    public void Manifest_JsonIsDeterministic()
    {
        if (!fx.Available) return;
        var again = ManifestScanner.Scan(fx.Root);
        Assert.Equal(fx.Manifest!.ToJson(), again.ToJson());
        Assert.Equal(again.FileCount, Manifest.FromJson(again.ToJson()).Files.Count);
    }

    [Fact]
    public void Db_BuildTwice_IdenticalTableHashesAndBytes()
    {
        if (!fx.Available) return;
        var a = KnowledgeStore.TableHashes(fx.DbA);
        var b = KnowledgeStore.TableHashes(fx.DbB);
        Assert.Equal(a, b);
        Assert.Contains("section", a.Keys);
        Assert.Equal(File.ReadAllBytes(fx.DbA), File.ReadAllBytes(fx.DbB));
    }

    [Fact]
    public void Section_UnitWeight_MatchesTheoretical_HBeam()
    {
        if (!fx.Available) return;
        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(fx.DbA, SqliteOpenMode.ReadOnly));
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT spec, m2, m3, m4, m5, m6, unit_weight FROM section WHERE family = 'H-BEAM'";
        using var r = cmd.ExecuteReader();
        var n = 0;
        var bad = new List<string>();
        while (r.Read())
        {
            double h = r.GetDouble(1), b = r.GetDouble(2), tw = r.GetDouble(3), tf = r.GetDouble(4), rr = r.GetDouble(5), w = r.GetDouble(6);
            var areaMm2 = 2 * b * tf + (h - 2 * tf) * tw + (4 - Math.PI) * rr * rr;
            var theoretical = areaMm2 * 7.85 / 1000.0; // kg/m
            n++;
            if (Math.Abs(theoretical - w) / w > 0.03)
            {
                bad.Add($"{r.GetString(0)}: table {w} vs A*7.85 {theoretical:F2}");
            }
        }

        Assert.True(n > 50, $"only {n} H-BEAM rows");
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    [Fact]
    public void Search_H400_FindsH400x200x8x13First()
    {
        if (!fx.Available) return;
        using var s = new KnowledgeStore(fx.DbA);
        var hits = s.Search("H400", "section", 10);
        Assert.Equal("H-BEAM/H400x200x8x13", hits[0].Key);
        Assert.Contains(s.Search("H-400x200x8x13"), h => h.Key == "H-BEAM/H400x200x8x13" && h.MatchedBy == "exact");
    }

    [Fact]
    public void Search_Korean_ExactAndFts()
    {
        if (!fx.Available) return;
        using var s = new KnowledgeStore(fx.DbA);
        Assert.Contains(s.Search("에이치빔 400"), h => h.Key == "H-BEAM/H400x200x8x13");
        var bolts = s.Search("고장력볼트", "bolt", 5);
        Assert.NotEmpty(bolts);
        Assert.All(bolts, h => Assert.StartsWith("fts", h.MatchedBy));
        Assert.Contains(s.Search("배치 상세", "doc_chunk"), h => h.Key == "manual-p3-1");
        Assert.Contains(s.Search("HTB M20", "bolt"), h => h.Key.StartsWith("TS M20", StringComparison.Ordinal));
    }

    [Fact]
    public void Graph_GetAndNeighbors()
    {
        if (!fx.Available) return;
        using var s = new KnowledgeStore(fx.DbA);
        var sec = s.Get("section", "H-BEAM/H400x200x8x13");
        Assert.NotNull(sec);
        Assert.Contains(s.Neighbors(sec!.Id, "family"), n => n.Node.Kind == "family" && n.Node.Key == "H-BEAM");
        var alias = s.Get("command_alias", "HB")!;
        Assert.Contains(s.Neighbors(alias.Id, depth: 2), n => n.Node.Kind == "palette_item" && n.Depth == 2);
        var block = s.Get("block", "HTB-M20")!;
        Assert.Contains(s.Neighbors(block.Id, "on_layer"), n => n.Node.Key == "HS-BOLT");
    }

    private static long Scalar(string db, string sql)
    {
        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(db, SqliteOpenMode.ReadOnly));
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Real_BlockCount114_AndPreviewSvgStored()
    {
        if (!fx.RealAvailable) return;
        Assert.Equal(114, Scalar(fx.RealA, "SELECT COUNT(*) FROM block"));
        Assert.True(Scalar(fx.RealA, "SELECT COUNT(*) FROM block WHERE preview_svg LIKE '<svg%'") >= 100);
        Assert.True(Scalar(fx.RealA, "SELECT COUNT(*) FROM mline_style") > 0);
        Assert.True(Scalar(fx.RealA, "SELECT COUNT(*) FROM font_map") > 0);
        Assert.Equal(fx.DocChunkCount, Scalar(fx.RealA, "SELECT COUNT(*) FROM doc_chunk"));
    }

    [Fact]
    public void Real_PaletteToBlockEdges_AtLeast50_ExistingBlocksOnly()
    {
        if (!fx.RealAvailable) return;
        // Only palette tools whose block exists as a block DWG get an edge (no guessed targets); 55 on the legacy tree.
        Assert.True(Scalar(fx.RealA, "SELECT COUNT(*) FROM edge WHERE rel = 'inserts'") >= 50);
        Assert.Equal(0, Scalar(fx.RealA, "SELECT COUNT(*) FROM edge e JOIN node d ON d.id = e.dst WHERE e.rel = 'inserts' AND d.kind <> 'block'"));
    }

    [Fact]
    public void Real_EveryEdgeHasEvidence()
    {
        if (!fx.RealAvailable) return;
        Assert.True(Scalar(fx.RealA, "SELECT COUNT(*) FROM edge") > 1000);
        Assert.Equal(0, Scalar(fx.RealA, "SELECT COUNT(*) FROM edge WHERE evidence_source_file IS NULL OR evidence_source_file = '' OR evidence_note IS NULL OR evidence_note = ''"));
        foreach (var rel in new[] { "inserts", "alias_of", "calls_lisp", "on_layer", "family", "spliced_with" })
        {
            Assert.True(Scalar(fx.RealA, $"SELECT COUNT(*) FROM edge WHERE rel = '{rel}'") > 0, rel);
        }
    }

    [Fact]
    public void Real_BuildTwice_Identical()
    {
        if (!fx.RealAvailable) return;
        Assert.Equal(KnowledgeStore.TableHashes(fx.RealA), KnowledgeStore.TableHashes(fx.RealB));
        Assert.Equal(File.ReadAllBytes(fx.RealA), File.ReadAllBytes(fx.RealB));
    }

    [Fact]
    public void Real_SearchBolt_ReturnsBlockOrCommand()
    {
        if (!fx.RealAvailable) return;
        using var s = new KnowledgeStore(fx.RealA);
        Assert.Contains(s.Search("볼트", null, 50), h => h.Kind is "block" or "command");
    }

    [Fact]
    public void Real_Neighbors_FromBlock_ReturnPaletteItems()
    {
        if (!fx.RealAvailable) return;
        using var s = new KnowledgeStore(fx.RealA);
        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(fx.RealA, SqliteOpenMode.ReadOnly));
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT d.key FROM edge e JOIN node d ON d.id = e.dst WHERE e.rel = 'inserts' ORDER BY d.key LIMIT 1";
        var blockKey = (string)cmd.ExecuteScalar()!;
        var node = s.Get("block", blockKey)!;
        var hits = s.Neighbors(node.Id, "inserts");
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal("palette_item", h.Node.Kind));
    }

    [Theory]
    [InlineData("H-400 x 200", "H400X200")]
    [InlineData("%%C190.7*5", "Φ190.7X5")]
    [InlineData("ts m20*60", "TSM20X60")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, SpecAliases.Normalize(input));
}
