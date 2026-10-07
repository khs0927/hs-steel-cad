using HsSteel.Knowledge;
using Microsoft.Data.Sqlite;

namespace HsSteel.Tests;

/// <summary>Content-gap closure (VBA / xlsx formulas / PDF OCR). Skips when the DB predates the content pass.</summary>
public class ContentGapTests
{
    private static SqliteConnection? Open()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var db = Path.Combine(d.FullName, "out", "hs_assets.db");
            if (File.Exists(Path.Combine(d.FullName, "HsSteel.sln")) && File.Exists(db))
            {
                var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(db, SqliteOpenMode.ReadOnly));
                cn.Open();
                return cn;
            }
        }

        return null;
    }

    private static long Scalar(SqliteConnection cn, string sql)
    {
        using var q = cn.CreateCommand();
        q.CommandText = sql;
        return Convert.ToInt64(q.ExecuteScalar());
    }

    [Fact]
    public void VbaAndFormulaChunksAndProceduresArePresent()
    {
        using var cn = Open();
        if (cn is null || Scalar(cn, "SELECT count(*) FROM sqlite_master WHERE name='vba_procedure'") == 0)
        {
            return;
        }

        Assert.True(Scalar(cn, "SELECT count(*) FROM doc_chunk WHERE kind='vba'") > 0);
        Assert.True(Scalar(cn, "SELECT count(*) FROM doc_chunk WHERE kind='xlsx_formula'") > 0);
        Assert.True(Scalar(cn, "SELECT count(*) FROM vba_procedure") > 0);
        Assert.Equal(0, Scalar(cn, "SELECT count(*) FROM vba_procedure WHERE line_count < 1 OR name = '' OR signature = ''"));
        // every kind='vba' chunk carries its vector so semantic search covers it
        Assert.Equal(0, Scalar(cn, "SELECT count(*) FROM doc_chunk c LEFT JOIN doc_chunk_vec v ON v.chunk_id=c.id WHERE c.kind IN ('vba','xlsx_formula','pdf_ocr') AND v.chunk_id IS NULL"));
    }
}
