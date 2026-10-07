using System.Globalization;
using System.Text;

namespace HsSteel.Assets.Templates;

/// <summary>One lisp-style line <c>("key" value)</c> / <c>("key" . "value")</c> / <c>("MARK" "M1" 350 ...)</c>.</summary>
public sealed record DatEntry(int Ord, string Key, string? Value, string ValueKind, IReadOnlyList<string> Atoms);

/// <summary>A legacy .dat file in either shape: lisp list lines (settings/member rows) or a plain line list (Base_*/Splice_* parameter files).</summary>
public sealed record DatFile(string Shape, string? Header, string? Name, IReadOnlyList<DatEntry> Entries, IReadOnlyList<string> Lines);

public static class DatFiles
{
    public const string Alist = "alist";
    public const string LineList = "linelist";

    static DatFiles() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Decode(byte[] bytes)
    {
        try
        {
            var s = new UTF8Encoding(false, true).GetString(bytes);
            return s.Length > 0 && s[0] == '﻿' ? s[1..] : s;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(949).GetString(bytes);
        }
    }

    public static DatFile Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var firstData = lines.FindIndex(l => l.TrimStart().StartsWith('('));
        if (firstData < 0)
        {
            // plain line list: first line is the name, the rest are values
            return new DatFile(LineList, null, lines.Count > 0 ? lines[0].Trim() : null, [], lines.Skip(1).Select(l => l.Trim()).ToList());
        }

        string? header = firstData > 0 ? lines[0].Trim() : null;
        var entries = new List<DatEntry>();
        var ord = 0;
        for (var i = firstData; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            var atoms = ReadList(line, out var dotted);
            if (atoms.Count == 0)
            {
                continue;
            }

            var key = atoms[0].Text;
            var rest = atoms.Skip(1).ToList();
            string? value = null;
            var kind = "row";
            if (rest.Count == 1)
            {
                value = rest[0].Text;
                kind = rest[0].Kind;
            }
            else if (rest.Count == 0)
            {
                kind = "flag";
            }

            _ = dotted;
            entries.Add(new DatEntry(ord++, key, value, kind, rest.Select(a => a.Text).ToList()));
        }

        return new DatFile(Alist, header, null, entries, []);
    }

    private readonly record struct Atom(string Text, string Kind);

    // Reads one parenthesised list; strings keep their text without quotes, '.' dotted-pair markers are dropped.
    private static List<Atom> ReadList(string s, out bool dotted)
    {
        dotted = false;
        var atoms = new List<Atom>();
        var i = s.IndexOf('(');
        if (i < 0)
        {
            return atoms;
        }

        i++;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == ')')
            {
                break;
            }
            else if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                    }

                    sb.Append(s[i]);
                    i++;
                }

                i++;
                atoms.Add(new Atom(sb.ToString(), "string"));
            }
            else
            {
                var j = i;
                while (j < s.Length && !char.IsWhiteSpace(s[j]) && s[j] != ')' && s[j] != '"')
                {
                    j++;
                }

                var tok = s[i..j];
                i = j;
                if (tok == ".")
                {
                    dotted = true;
                    continue;
                }

                atoms.Add(new Atom(tok, double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? "number" : "symbol"));
            }
        }

        return atoms;
    }
}
