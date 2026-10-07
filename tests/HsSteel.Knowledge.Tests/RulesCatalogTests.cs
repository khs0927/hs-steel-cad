using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HsSteel.Knowledge.Rules;
using Microsoft.Data.Sqlite;

namespace HsSteel.Knowledge.Tests;

public class RulesCatalogTests
{
    private static readonly RulesCatalog Cat = RulesCatalog.Instance;

    [Fact]
    public void EveryRuleIsGradedWithLocatedEvidence()
    {
        Assert.True(Cat.Count >= 135);
        Assert.Equal(Cat.Count, Cat.All.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var r in Cat.All)
        {
            Assert.Contains(r.Trust, SteelRule.TrustLevels);
            Assert.NotEmpty(r.Evidence);
            Assert.All(r.Evidence, e => Assert.False(string.IsNullOrWhiteSpace(e.Locator), $"{r.Id} evidence without locator"));
            Assert.NotNull(r.Verification);
            Assert.Contains(r.EngineStatus, new[] { "implemented", "differs", "missing", "n/a" });
            if (r.Trust == "verified")
            {
                Assert.True(r.Verification!.Result == "pass", $"{r.Id} verified but result {r.Verification.Result}");
            }

            if (r.Trust == "inferred")
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Rationale), r.Id);
                Assert.Equal("claude-opus-5-5", r.InferredBy);
            }
        }
    }

    [Fact]
    public void FormulasCarryMachineReadableExpressions()
    {
        foreach (var id in new[] { "BL-001", "WT-002", "WT-003", "SPL-002", "QT-001", "CUT-003" })
        {
            var f = Cat.Get(id)!.Formula;
            Assert.True(f.HasValue, id);
            Assert.True(f!.Value.TryGetProperty("expr", out _), id);
        }

        var add = Cat.Get("BL-002")!.Formula!.Value.GetProperty("table").GetProperty("add");
        Assert.Equal(25, add.GetProperty("16").GetInt32());
        Assert.Equal(30, add.GetProperty("20").GetInt32());
        Assert.Equal(35, add.GetProperty("22").GetInt32());
    }

    // Independent cross-check: 자재산출-2017.xlsm member!L2/M2 cache H300*300*10*15 (r=18) as 94 kg/m and 1.78 m2/m.
    [Fact]
    public void WeightAndPaintFormulasReproduceWorkbookCache()
    {
        Assert.Equal(94.0, HWeight(300, 300, 10, 15, 18));
        Assert.Equal(1.78, Paint(300, 300, 10));
        Assert.Equal(49.6, HWeight(350, 175, 7, 11, 14));
    }

    [Fact]
    public void BoltLengthRuleReproducesProgramSpliceTables()
    {
        var dir = @"C:\HS-STEEL\HSSTEEL\attributes";
        if (!Directory.Exists(dir))
        {
            return; // asset tree not present on this machine
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var add = new Dictionary<int, int> { [16] = 25, [20] = 30, [22] = 35 };
        var tok = new Regex("\"([^\"]*)\"|(\\S+)");
        int n = 0, ok = 0;
        foreach (var path in Directory.GetFiles(dir, "SCSS-*.dat").Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadAllLines(path, Encoding.GetEncoding(949)))
            {
                var f = tok.Matches(line.Trim('(', ')', ' ')).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();
                if (f.Count < 27 || f[0] == "규격")
                {
                    continue;
                }

                var d = f[0].Split('x', 'X', '*').Skip(0).Select(s => double.Parse(Regex.Replace(s, "[^0-9.]", ""), CultureInfo.InvariantCulture)).ToArray();
                double S(int i) => double.Parse(f[i], CultureInfo.InvariantCulture);
                foreach (var (grip, name) in new[] { (d[3] + S(11) + S(12), f[24]), (d[2] + 2 * S(1), f[26]) })
                {
                    var m = Regex.Match(name, @"M(\d+)\*(\d+)");
                    var dia = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    var len = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    n++;
                    ok += Math.Ceiling((grip + add[dia]) / 5 - 1e-9) * 5 == len ? 1 : 0;
                }
            }
        }

        Assert.True(n >= 300, $"only {n} bolt lines");
        Assert.Equal(n, ok);
    }

    [Fact]
    public void DbMaterializesRulesWhenBuilt()
    {
        var db = FindDb();
        if (db is null)
        {
            return;
        }

        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(db, SqliteOpenMode.ReadOnly));
        cn.Open();
        long Scalar(string sql)
        {
            using var c = cn.CreateCommand();
            c.CommandText = sql;
            return Convert.ToInt64(c.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        if (Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'rule'") == 0)
        {
            return; // DB predates rules ingest
        }

        Assert.Equal(Cat.Count, Scalar("SELECT COUNT(*) FROM rule"));
        Assert.Equal(Cat.Count, Scalar("SELECT COUNT(*) FROM node WHERE kind = 'rule'"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM node n WHERE n.kind = 'rule' AND NOT EXISTS (SELECT 1 FROM edge e WHERE e.src = n.id AND e.rel = 'evidenced_by')"));
        Assert.True(Scalar("SELECT COUNT(*) FROM edge WHERE rel = 'governs'") > 50);
        Assert.Equal(Cat.Count, Scalar("SELECT COUNT(*) FROM search_fts WHERE kind = 'rule'"));
    }

    private static double HWeight(double h, double b, double tw, double tf, double r) =>
        Math.Round((2 * b * tf + (h - 2 * tf) * tw + (4 - Math.PI) * r * r) * 0.00785, 1);

    private static double Paint(double h, double b, double tw) => Math.Round((2 * h + 4 * b - 2 * tw) / 1000, 3);

    private static string? FindDb()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "out", "hs_assets.db");
            if (File.Exists(c))
            {
                return c;
            }
        }

        return null;
    }
}
