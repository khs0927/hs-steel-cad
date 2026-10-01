using System.Globalization;
using System.Text;

namespace HsSteel.Assets;

/// <summary>One row of an HS-STEEL attributes/*.dat section table (all columns kept).</summary>
/// <param name="Spec">Designation, e.g. "H300x150x6.5x9".</param>
/// <param name="Shape">Shape code (H, L, C, ...).</param>
/// <param name="M">M2..M7 dimensions in mm, meaning depends on the family (H: h, b, tw, tf, r).</param>
/// <param name="UnitWeight">kg/m.</param>
/// <param name="PaintArea">m짼/m.</param>
/// <param name="Color">ACI color used by HS-STEEL for this family.</param>
/// <param name="Family">Family name (file name), e.g. "H-BEAM".</param>
public sealed record SectionRecord(
    string Spec, string Shape, IReadOnlyList<double> M, double UnitWeight, double PaintArea, int Color, string Family);

/// <summary>Reads HS-STEEL section tables (CP949, whitespace separated, first line is a header).</summary>
public static class SectionTable
{
    static SectionTable() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding Cp949 => Encoding.GetEncoding(949);

    public static IReadOnlyList<SectionRecord> Load(string path)
    {
        var family = Path.GetFileNameWithoutExtension(path);
        return Parse(File.ReadAllText(path, Cp949), family);
    }

    public static IReadOnlyList<SectionRecord> Parse(string text, string family)
    {
        var rows = new List<SectionRecord>();
        var lines = text.Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var f = lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 11)
            {
                continue;
            }

            // Header says M1..M7 but M1 is the shape code: 6 numeric dims follow.
            var m = f.Skip(2).Take(6).Select(Num).ToArray();
            rows.Add(new SectionRecord(f[0], f[1], m, Num(f[8]), Num(f[9]), (int)Num(f[10]), f.Length > 11 ? f[11] : family));
        }

        return rows;
    }

    private static double Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}

/// <summary>Reads Project.dat: first line is an id, then LISP pairs ("KEY" value).</summary>
public static class ProjectSettings
{
    public static IReadOnlyDictionary<string, string> Load(string path) => Parse(File.ReadAllText(path, SectionTable.Cp949));

    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("(\"", StringComparison.Ordinal) || !line.EndsWith(')'))
            {
                continue;
            }

            var close = line.IndexOf('"', 2);
            if (close < 0)
            {
                continue;
            }

            var key = line[2..close];
            var value = line[(close + 1)..^1].Trim().Trim('"');
            map[key] = value;
        }

        return map;
    }
}

