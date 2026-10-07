using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HsSteel.Assets.Legacy;

/// <summary>One AutoLISP association entry: ("KEY" . "v") or ("KEY" a b ...).</summary>
public sealed record MxxEntry(string Key, IReadOnlyList<object> Values, bool IsDotted);

/// <summary>
/// Legacy HS-STEEL ".Mxx" macro option file (AutoSaveLoad.M34, For-Macro70.M77 ...).
/// NOTE: these are per-macro dialog settings, NOT member (mark/spec/length/qty) lists. See MxxFormat.md.
/// </summary>
public sealed class MxxFile
{
    public string Path { get; init; } = "";
    public string Stamp { get; init; } = "";               // line 1, "NNNNNNNNN-yyyyMMddHHmm"
    public DateTime? StampTime { get; init; }
    public IReadOnlyList<MxxEntry> Entries { get; init; } = [];
    public int MacroNo { get; init; }                      // from extension, e.g. ".M34" -> 34 (0 if none)

    public MxxEntry? Find(string key) =>
        Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}

public static class MxxReader
{
    private static readonly Regex StampRx = new(@"^(\d+)-(\d{12})$", RegexOptions.Compiled);
    private static readonly Regex EntryRx = new(@"^\(\s*""(?<k>[^""]*)""\s*(?<dot>\.)?\s*(?<v>.*)\)\s*$", RegexOptions.Compiled);
    private static readonly Regex TokRx = new(@"""([^""]*)""|(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);

    public static MxxFile Read(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(949).GetString(File.ReadAllBytes(path));
        var f = Parse(text);
        var ext = System.IO.Path.GetExtension(path);
        int no = 0;
        if (ext.Length > 2 && (ext[1] == 'M' || ext[1] == 'm')) int.TryParse(ext.AsSpan(2), out no);
        return new MxxFile { Path = path, Stamp = f.Stamp, StampTime = f.StampTime, Entries = f.Entries, MacroNo = no };
    }

    public static MxxFile Parse(string text)
    {
        string stamp = "";
        DateTime? when = null;
        var entries = new List<MxxEntry>();
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0) continue;
            var sm = StampRx.Match(l);
            if (sm.Success && stamp.Length == 0)
            {
                stamp = l;
                if (DateTime.TryParseExact(sm.Groups[2].Value, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) when = t;
                continue;
            }
            var m = EntryRx.Match(l);
            if (!m.Success) continue;
            var vals = new List<object>();
            foreach (Match t in TokRx.Matches(m.Groups["v"].Value))
            {
                if (t.Groups[1].Success) vals.Add(t.Groups[1].Value);
                else if (double.TryParse(t.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) vals.Add(d);
            }
            entries.Add(new MxxEntry(m.Groups["k"].Value, vals, m.Groups["dot"].Success));
        }
        return new MxxFile { Stamp = stamp, StampTime = when, Entries = entries };
    }
}
