using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using HsSteel.Knowledge;
using HsSteel.Knowledge.Ingest;
using Microsoft.Data.Sqlite;

namespace HsSteel.Tests;

public class TemplateIngestTests
{
    private static string? Repo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "HsSteel.sln")))
            {
                return d.FullName;
            }
        }

        return null;
    }

    private static byte[] Bmp24(int w, int h, Func<int, int, (byte R, byte G, byte B)> px)
    {
        var stride = (w * 3 + 3) / 4 * 4;
        var data = new byte[54 + stride * h];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), w);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), h);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 24);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (r, g, b) = px(x, h - 1 - y); // BMP rows are bottom-up; px takes top-down y
                var o = 54 + y * stride + x * 3;
                data[o] = b;
                data[o + 1] = g;
                data[o + 2] = r;
            }
        }

        return data;
    }

    private static (int W, int H, byte[] Rgba) DecodePng(byte[] png)
    {
        Assert.True(png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        var w = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        var h = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        Assert.Equal((8, 6), (png[24], png[25]));
        var idat = new MemoryStream();
        for (var p = 8; p < png.Length;)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(p));
            var type = System.Text.Encoding.ASCII.GetString(png, p + 4, 4);
            if (type == "IDAT")
            {
                idat.Write(png, p + 8, len);
            }

            p += 12 + len;
        }

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new MemoryStream();
        z.CopyTo(raw);
        var bytes = raw.ToArray();
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            Assert.Equal(0, bytes[y * (w * 4 + 1)]);
            Buffer.BlockCopy(bytes, y * (w * 4 + 1) + 1, rgba, y * w * 4, w * 4);
        }

        return (w, h, rgba);
    }

    [Fact]
    public void Bmp24ConvertsToPngWithSamePixels()
    {
        var bmp = Bmp24(3, 2, (x, y) => ((byte)(x * 80), (byte)(y * 100), (byte)(x + y)));
        var img = IconCodec.Normalize(bmp);
        Assert.Equal(("image/png", 3, 2, 24, true), (img.Mime, img.Width, img.Height, img.BitsPerPixel, img.Normalized));
        var (w, h, rgba) = DecodePng(img.Data);
        Assert.Equal((3, 2), (w, h));
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                var o = (y * 3 + x) * 4;
                Assert.Equal(new byte[] { (byte)(x * 80), (byte)(y * 100), (byte)(x + y), 255 }, rgba[o..(o + 4)]);
            }
        }

        Assert.Equal(img.Data, IconCodec.Normalize(bmp).Data);
    }

    [Fact]
    public void PngPassesThroughAndGarbageIsRejected()
    {
        var png = IconCodec.EncodePng(2, 2, new byte[16]);
        var img = IconCodec.Normalize(png);
        Assert.False(img.Normalized);
        Assert.Equal((2, 2), (img.Width, img.Height));
        Assert.Same(png, img.Data);
        Assert.Throws<InvalidDataException>(() => IconCodec.Normalize(new byte[40]));
    }

    [Fact]
    public void LegacyIconsAllConvert()
    {
        var dir = @"C:\HS-STEEL\HSSTEEL\Icons";
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var f in Directory.GetFiles(dir, "*.bmp"))
        {
            var img = IconCodec.Normalize(File.ReadAllBytes(f));
            Assert.True(img.Width > 0 && img.Height > 0, f);
        }
    }

    [Fact]
    public void IngestIsByteIdenticalAndWritesCoverage()
    {
        var repo = Repo();
        var root = ManifestScanner.DefaultRoot();
        if (repo is null || !Directory.Exists(root) || !File.Exists(Path.Combine(repo, "assets", "manifest.json")))
        {
            return;
        }

        var manifest = Manifest.Load(Path.Combine(repo, "assets", "manifest.json"));
        var tmp = Path.Combine(Path.GetTempPath(), "hs_ingest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var dbs = new[] { Path.Combine(tmp, "a.db"), Path.Combine(tmp, "b.db") };
            IngestSummary? last = null;
            foreach (var db in dbs)
            {
                new KnowledgeDbBuilder(manifest).Build(db);
                last = AssetIngest.Apply(db, manifest, root, Path.Combine(tmp, "coverage.json"));
            }

            Assert.Equal(File.ReadAllBytes(dbs[0]), File.ReadAllBytes(dbs[1]));
            Assert.Equal(416, last!.Counts["slide"]);
            Assert.Equal(0, last.Counts["ingest_failure"]);
            Assert.True(last.Counts["icon"] >= 1000);
            Assert.True(last.Counts["dialog"] >= 3);
            Assert.True(File.Exists(last.CoveragePath));

            // idempotent: applying again on the same file leaves identical logical content
            var before = Fingerprint(dbs[1]);
            AssetIngest.Apply(dbs[1], manifest, root, Path.Combine(tmp, "coverage.json"));
            Assert.Equal(before, Fingerprint(dbs[1]));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(tmp, true);
        }
    }

    private static string Fingerprint(string db)
    {
        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(db, SqliteOpenMode.ReadOnly));
        cn.Open();
        var tables = new List<string>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'search_fts%' ORDER BY name";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                tables.Add(r.GetString(0));
            }
        }

        tables.Add("search_fts");
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var t in tables)
        {
            sha.AppendData(System.Text.Encoding.UTF8.GetBytes(t));
            using var q = cn.CreateCommand();
            q.CommandText = $"SELECT * FROM \"{t}\" ORDER BY rowid";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                for (var i = 0; i < r.FieldCount; i++)
                {
                    sha.AppendData(r.IsDBNull(i) ? [0] : r.GetValue(i) is byte[] bl ? bl : System.Text.Encoding.UTF8.GetBytes(Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!));
                }
            }
        }

        return Convert.ToHexString(sha.GetHashAndReset());
    }

    [Fact]
    public void EveryIngestRowIsCoveredOrExplicitlyDeferred()
    {
        var repo = Repo();
        var dbPath = repo is null ? null : Path.Combine(repo, "out", "hs_assets.db");
        if (repo is null || !File.Exists(dbPath) || !File.Exists(Path.Combine(repo, "assets", "manifest.json")))
        {
            return;
        }

        var manifest = Manifest.Load(Path.Combine(repo, "assets", "manifest.json"));
        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(dbPath!, SqliteOpenMode.ReadOnly));
        cn.Open();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='slide'";
            if (Convert.ToInt64(q.ExecuteScalar()) == 0)
            {
                return; // db built before asset ingest existed
            }
        }

        var rows = Coverage.Compute(cn, manifest, ManifestScanner.DefaultRoot());
        Assert.Equal(manifest.Files.Count(f => f.Disposition == ManifestScanner.Ingest), rows.Count);
        Assert.Empty(rows.Where(r => r.Status == "uncovered").Select(r => r.RelPath));
        Assert.All(rows.Where(r => r.Status == "deferred"), r => Assert.False(string.IsNullOrWhiteSpace(r.Reason), r.RelPath));
        Assert.All(rows.Where(r => r.Status == "covered"), r => Assert.NotEmpty(r.Tables));

        var covPath = Path.Combine(repo, "out", "knowledge", "coverage.json");
        if (File.Exists(covPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(covPath));
            Assert.Equal(0, doc.RootElement.GetProperty("uncovered").GetInt32());
            Assert.Equal(100.0, doc.RootElement.GetProperty("resolvedPercent").GetDouble());
        }
    }

    [Fact]
    public void DbHoldsSlidesIconsDialogsAndTemplates()
    {
        var repo = Repo();
        var dbPath = repo is null ? null : Path.Combine(repo, "out", "hs_assets.db");
        if (repo is null || !File.Exists(dbPath))
        {
            return;
        }

        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(dbPath!, SqliteOpenMode.ReadOnly));
        cn.Open();
        long Count(string sql)
        {
            using var q = cn.CreateCommand();
            q.CommandText = sql;
            return Convert.ToInt64(q.ExecuteScalar());
        }

        if (Count("SELECT count(*) FROM sqlite_master WHERE name='slide'") == 0)
        {
            return;
        }

        Assert.Equal(416, Count("SELECT count(*) FROM slide"));
        Assert.True(Count("SELECT count(*) FROM slide WHERE svg LIKE '<svg%' AND width > 0") == 416);
        Assert.True(Count("SELECT count(*) FROM icon WHERE mime='image/png' AND length(data) > 60") >= 1000);
        Assert.True(Count("SELECT count(*) FROM edge WHERE rel='has_icon'") > 0);
        Assert.True(Count("SELECT count(*) FROM dialog WHERE name='dim_edit'") == 1);
        Assert.True(Count("SELECT count(*) FROM dialog_field WHERE key='dum1'") >= 1);
        Assert.True(Count("SELECT count(*) FROM project_default WHERE key='WDGAP'") >= 2);
        Assert.True(Count("SELECT count(*) FROM drafting_style WHERE category='layer'") > 0);
        Assert.True(Count("SELECT count(*) FROM workbook_sheet WHERE name='member'") >= 1);
    }
}
