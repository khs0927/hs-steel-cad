using System.Text.Json;
using HsSteel.Knowledge;
using HsSteel.Knowledge.Reborn;
using Microsoft.Data.Sqlite;

namespace HsSteel.Knowledge.Tests;

/// <summary>Builds a sections-only DB twice from C:\HS-STEEL and applies the HS-STEEL_REBORN post-step to both.</summary>
public sealed class RebornFixture : IDisposable
{
    public string LegacyRoot { get; } = ManifestScanner.DefaultRoot();
    public string RebornRoot { get; } = Environment.GetEnvironmentVariable("HS_STEEL_REBORN") is { Length: > 0 } r ? r : RebornManifest.DefaultRoot;
    public bool Available => Directory.Exists(LegacyRoot) && Directory.Exists(RebornRoot);
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "hs-reborn-tests-" + Guid.NewGuid().ToString("N"));
    public string DbA => Path.Combine(Dir, "a", "hs_assets.db");
    public string DbB => Path.Combine(Dir, "b", "hs_assets.db");
    public RebornReport? Report { get; }

    public RebornFixture()
    {
        if (!Available)
        {
            return;
        }

        var m = ManifestScanner.Scan(LegacyRoot);
        foreach (var p in new[] { DbA, DbB })
        {
            new KnowledgeDbBuilder(m).IngestSections(LegacyRoot)
                .IngestCommands([new CommandRecord("LAYTHW", "도면층 동결해제", "^C^C_laythw", "HSSTEEL/support/x.cuix")])
                .IngestBlocks([new BlockRecord("BOLT-DATA-TABLE", "HSSTEEL/block/BOLT-DATA-TABLE.dwg", 0, 0, 0, 0, 0, 1, 1, 1, null, [], ["0"])])
                .Build(p);
            Report = RebornIngest.Apply(p, RebornRoot);
        }
    }

    public static string Repo()
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

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public sealed class RebornTests(RebornFixture fx) : IClassFixture<RebornFixture>
{
    private static readonly string ManifestPath = Path.Combine(RebornFixture.Repo(), "assets", "reborn_manifest.json");

    [Fact]
    public void Manifest_RowsEqualFilesOnDisk()
    {
        if (!fx.Available || !File.Exists(ManifestPath))
        {
            return;
        }

        var m = RebornManifest.Load(ManifestPath);
        var disk = RebornManifest.ListFiles(fx.RebornRoot);
        Assert.Equal(disk.Count, m.FileCount);
        Assert.Equal(disk.Count, m.Files.Count);
        Assert.Equal(disk.Select(d => d.RelPath), m.Files.Select(f => f.RelPath));
        Assert.Equal(m.Files.Count, m.Counts.Values.Sum());
        Assert.All(m.Files, f => Assert.Contains(f.Disposition, new[] { "ingest", "reference", "excluded" }));
        Assert.All(m.Files.Where(f => f.Sha256 is null), f => Assert.True(f.Reason == RebornManifest.VenvReason || f.Reason == RebornManifest.DeviceNameReason, f.RelPath));
        Assert.All(m.Files.Where(f => f.Sha256 is not null), f => Assert.Equal(64, f.Sha256!.Length));
        Assert.Equal(RebornManifest.IngestFiles.Count(r => disk.Any(d => d.RelPath == r)), m.Files.Count(f => f.Disposition == "ingest"));
    }

    [Fact]
    public void Manifest_ToJson_RoundTripsByteIdentical()
    {
        if (!File.Exists(ManifestPath))
        {
            return;
        }

        var text = File.ReadAllText(ManifestPath);
        Assert.Equal(text, RebornManifest.Load(ManifestPath).ToJson());
    }

    [Theory]
    [InlineData(".venv/Lib/site-packages/numpy/__init__.py", "excluded")]
    [InlineData("engine/__pycache__/x.cpython-313.pyc", "excluded")]
    [InlineData("engine/.pytest_cache/v/cache/nodeids", "excluded")]
    [InlineData("com_probe3.ps1", "excluded")]
    [InlineData("focus2.ps1", "excluded")]
    [InlineData("uia_dlg.ps1", "excluded")]
    [InlineData("materials/nul", "excluded")]
    [InlineData("orch/bom_schema.json", "ingest")]
    [InlineData("orch/user_manual.md", "ingest")]
    [InlineData("engine/bom_engine.py", "reference")]
    [InlineData("work/fas/hs02/MACRO32.fas", "reference")]
    public void Classify_BulkRules(string rel, string disposition) => Assert.Equal(disposition, RebornManifest.Classify(rel).Disposition);

    [Fact]
    public void Db_RebuildWithReborn_ByteIdentical()
    {
        if (!fx.Available)
        {
            return;
        }

        Assert.Equal(File.ReadAllBytes(fx.DbA), File.ReadAllBytes(fx.DbB));
    }

    [Fact]
    public void DerivedSpec_EveryRowHasTrustAndNote_CitesPlanCorrections()
    {
        if (!fx.Available)
        {
            return;
        }

        using var cn = Open();
        var rows = Rows(cn, "SELECT source_file, trust, trust_note FROM derived_spec");
        Assert.True(rows.Count >= 25, "derived specs: " + rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Contains(r[1], new[] { "verified", "unverified", "refuted" });
            Assert.False(string.IsNullOrWhiteSpace(r[2]));
        });
        string Trust(string f) => rows.Single(r => r[0] == RebornIngest.RootPrefix + f)[1];
        Assert.Equal("refuted", Trust("orch/command_triage.json"));
        Assert.Equal("unverified", Trust("orch/command_triage_v2.json"));
        Assert.Equal("refuted", Trust("orch/weight_audit.json"));
        Assert.Equal("unverified", Trust("materials/weight_table.json"));
        Assert.Contains("ORCHESTRATION_PLAN.md", rows.Single(r => r[0].EndsWith("command_triage.json", StringComparison.Ordinal))[2]);
    }

    [Theory]
    [InlineData("자재산출서", "orch/bom_schema.json")]
    [InlineData("BomList", "orch/bomlist_spec.json")]
    [InlineData("절단", "orch/cut_plan_spec.json")]
    public void Search_ReturnsDerivedSpecs(string q, string expectedKey)
    {
        if (!fx.Available)
        {
            return;
        }

        using var store = new KnowledgeStore(fx.DbA);
        var hits = store.Search(q, "derived_spec", 5, SearchMode.Lexical);
        Assert.Contains(hits, h => h.Key == expectedKey);
        Assert.Contains(store.Search(q, null, 50, SearchMode.Lexical), h => h.Kind is "derived_spec" or "bom_sheet");
    }

    [Fact]
    public void WeightCrossCheck_Reported()
    {
        if (!fx.Available)
        {
            return;
        }

        using var cn = Open();
        var json = Rows(cn, "SELECT value FROM meta WHERE key = 'reborn_weight_crosscheck'").Single()[0];
        using var d = JsonDocument.Parse(json);
        Assert.True(d.RootElement.GetProperty("matched").GetInt32() > 100);
        Assert.True(d.RootElement.GetProperty("agreementPct").GetDouble() >= 0);
        Assert.Contains("Cross-check:", Rows(cn, "SELECT trust_note FROM derived_spec WHERE source_file LIKE '%weight_table.json'").Single()[0]);
    }

    [Fact]
    public void Graph_EvidenceBackedEdges()
    {
        if (!fx.Available)
        {
            return;
        }

        using var cn = Open();
        var rows = Rows(cn, """
            SELECT s.kind, s.key, e.rel, d.kind, d.key, e.evidence_source_file, e.evidence_note FROM edge e
            JOIN node s ON s.id = e.src JOIN node d ON d.id = e.dst
            WHERE d.kind IN ('catalog_entry','fas_function','dwg_dxf_entry') OR s.kind = 'bom_sheet'
            """);
        Assert.All(rows, r =>
        {
            Assert.StartsWith(RebornIngest.RootPrefix, r[5]);
            Assert.False(string.IsNullOrWhiteSpace(r[6]));
        });
        Assert.Contains(rows, r => r[0] == "command" && r[1] == "LAYTHW" && r[3] == "catalog_entry" && r[4] == "LAYTHW");
        Assert.Contains(rows, r => r[0] == "block" && r[1] == "BOLT-DATA-TABLE" && r[4] == "BOLT-DATA-TABLE.dxf");
        Assert.Equal(9, Rows(cn, "SELECT key FROM node WHERE kind = 'bom_sheet'").Count);
        Assert.Contains("자재산출서", Rows(cn, "SELECT key FROM node WHERE kind = 'bom_sheet'").Select(r => r[0]));
    }

    [Fact]
    public void MarkdownChunks_StoredAsRebornMd()
    {
        if (!fx.Available)
        {
            return;
        }

        using var cn = Open();
        var n = Rows(cn, "SELECT id FROM doc_chunk WHERE kind = 'reborn_md'").Count;
        Assert.True(n >= 10, "reborn_md chunks: " + n);
        Assert.Contains(Rows(cn, "SELECT id FROM doc_chunk WHERE kind = 'reborn_md'"), r => r[0].StartsWith("reborn/ORCHESTRATION_PLAN.md#", StringComparison.Ordinal));
    }

    private SqliteConnection Open()
    {
        var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(fx.DbA, SqliteOpenMode.ReadOnly));
        cn.Open();
        return cn;
    }

    private static List<string[]> Rows(SqliteConnection cn, string sql)
    {
        using var c = cn.CreateCommand();
        c.CommandText = sql;
        using var r = c.ExecuteReader();
        var l = new List<string[]>();
        while (r.Read())
        {
            l.Add(Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!).ToArray());
        }

        return l;
    }
}
