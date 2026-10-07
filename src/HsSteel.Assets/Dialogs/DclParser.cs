using System.Text;

namespace HsSteel.Assets.Dialogs;

/// <summary>One tile (widget) of a DCL definition. <see cref="Depth"/> 0 is the definition's own root tile.</summary>
public sealed record DclTile(int Depth, string Type, string? Key, string? Label, string? Default, IReadOnlyList<string> ListItems, IReadOnlyDictionary<string, string> Attributes, bool IsReference);

/// <summary>A top-level DCL definition: <c>name : base { ... }</c>. Kind is "dialog" when Base is dialog, otherwise "prototype".</summary>
public sealed record DclDefinition(string Name, string Base, string Kind, string? Label, IReadOnlyList<DclTile> Tiles);

public sealed record DclFile(IReadOnlyList<DclDefinition> Definitions, IReadOnlyList<string> Includes, IReadOnlyList<string> Warnings);

/// <summary>Tolerant parser for AutoCAD Dialog Control Language (.dcl): comments, @include, prototypes, nested tiles, attributes, tile references.</summary>
public static class DclParser
{
    static DclParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Decodes UTF-8 when valid, otherwise the Korean ANSI code page 949 (legacy HS-STEEL files).</summary>
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

    private enum T { Ident, Str, Num, Colon, Semi, Eq, LBrace, RBrace, Other, Eof }

    private readonly record struct Tok(T Kind, string Text, int Line);

    private static List<Tok> Lex(string src, List<string> includes)
    {
        var toks = new List<Tok>();
        var line = 1;
        for (var i = 0; i < src.Length;)
        {
            var c = src[i];
            if (c == '\n')
            {
                line++;
                i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/'))
                {
                    if (src[i] == '\n')
                    {
                        line++;
                    }

                    i++;
                }

                i = Math.Min(src.Length, i + 2);
            }
            else if (c == '@')
            {
                var j = i + 1;
                while (j < src.Length && (char.IsLetter(src[j]) || src[j] == '_'))
                {
                    j++;
                }

                var word = src[(i + 1)..j];
                i = j;
                if (word.Equals("include", StringComparison.OrdinalIgnoreCase))
                {
                    while (i < src.Length && src[i] != '"' && src[i] != '\n')
                    {
                        i++;
                    }

                    if (i < src.Length && src[i] == '"')
                    {
                        var (s, next) = ReadString(src, i);
                        includes.Add(s);
                        i = next;
                    }
                }
            }
            else if (c == '"')
            {
                var (s, next) = ReadString(src, i);
                toks.Add(new Tok(T.Str, s, line));
                i = next;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var j = i;
                while (j < src.Length && (char.IsLetterOrDigit(src[j]) || src[j] == '_'))
                {
                    j++;
                }

                toks.Add(new Tok(T.Ident, src[i..j], line));
                i = j;
            }
            else if (char.IsDigit(c) || ((c == '-' || c == '.' || c == '+') && i + 1 < src.Length && (char.IsDigit(src[i + 1]) || src[i + 1] == '.')))
            {
                var j = i + 1;
                while (j < src.Length && (char.IsDigit(src[j]) || src[j] is '.' or 'e' or 'E'))
                {
                    j++;
                }

                toks.Add(new Tok(T.Num, src[i..j], line));
                i = j;
            }
            else
            {
                toks.Add(new Tok(c switch { ':' => T.Colon, ';' => T.Semi, '=' => T.Eq, '{' => T.LBrace, '}' => T.RBrace, _ => T.Other }, c.ToString(), line));
                i++;
            }
        }

        toks.Add(new Tok(T.Eof, "", line));
        return toks;
    }

    private static (string Text, int Next) ReadString(string src, int start)
    {
        var sb = new StringBuilder();
        var i = start + 1;
        while (i < src.Length && src[i] != '"' && src[i] != '\n')
        {
            if (src[i] == '\\' && i + 1 < src.Length)
            {
                i++;
                sb.Append(src[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', var o => o });
            }
            else
            {
                sb.Append(src[i]);
            }

            i++;
        }

        return (sb.ToString(), i < src.Length && src[i] == '"' ? i + 1 : i);
    }

    public static DclFile Parse(string text)
    {
        var includes = new List<string>();
        var warnings = new List<string>();
        var toks = Lex(text, includes);
        var defs = new List<DclDefinition>();
        var p = 0;

        while (toks[p].Kind != T.Eof)
        {
            // name : base { ... }
            if (toks[p].Kind == T.Ident && toks[p + 1].Kind == T.Colon && toks[p + 2].Kind == T.Ident && toks[p + 3].Kind == T.LBrace)
            {
                var name = toks[p].Text;
                var bas = toks[p + 2].Text;
                p += 4;
                var tiles = new List<DclTile>();
                ParseBody(toks, ref p, 0, bas, tiles, warnings);
                var root = tiles.Count > 0 ? tiles[0] : null;
                defs.Add(new DclDefinition(name, bas, bas.Equals("dialog", StringComparison.OrdinalIgnoreCase) ? "dialog" : "prototype", root?.Label, tiles));
            }
            else
            {
                warnings.Add($"line {toks[p].Line}: unexpected '{toks[p].Text}' at top level");
                p++;
            }
        }

        return new DclFile(defs, includes, warnings);
    }

    // Parses attributes and child tiles until the closing brace; the first emitted tile is the root (type = bas).
    private static void ParseBody(List<Tok> toks, ref int p, int depth, string type, List<DclTile> output, List<string> warnings)
    {
        var attrs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var rootIndex = output.Count;
        output.Add(null!);
        var children = new List<DclTile>();

        while (toks[p].Kind != T.RBrace && toks[p].Kind != T.Eof)
        {
            var t = toks[p];
            if (t.Kind == T.Ident && toks[p + 1].Kind == T.Eq)
            {
                p += 2;
                var val = new StringBuilder();
                while (toks[p].Kind is not (T.Semi or T.RBrace or T.Eof))
                {
                    if (val.Length > 0 && toks[p].Kind != T.Str)
                    {
                        val.Append(' ');
                    }

                    val.Append(toks[p].Text);
                    p++;
                }

                attrs[t.Text] = val.ToString();
                if (toks[p].Kind == T.Semi)
                {
                    p++;
                }
            }
            else if (t.Kind == T.Colon && toks[p + 1].Kind == T.Ident)
            {
                var childType = toks[p + 1].Text;
                p += 2;
                if (toks[p].Kind == T.LBrace)
                {
                    p++;
                    ParseBody(toks, ref p, depth + 1, childType, children, warnings);
                }
                else
                {
                    if (toks[p].Kind == T.Semi)
                    {
                        p++;
                    }

                    children.Add(MakeTile(depth + 1, childType, new SortedDictionary<string, string>(StringComparer.Ordinal), false));
                }
            }
            else if (t.Kind == T.Ident && toks[p + 1].Kind == T.Semi)
            {
                // reference to a prototype/subassembly, e.g. "ok_cancel ;"
                children.Add(MakeTile(depth + 1, t.Text, new SortedDictionary<string, string>(StringComparer.Ordinal), true));
                p += 2;
            }
            else if (t.Kind == T.Ident && toks[p + 1].Kind == T.Colon && toks[p + 2].Kind == T.Ident && toks[p + 3].Kind == T.LBrace)
            {
                // named inline definition (rare): name : type { ... }
                var childType = toks[p + 2].Text;
                p += 4;
                ParseBody(toks, ref p, depth + 1, childType, children, warnings);
            }
            else
            {
                warnings.Add($"line {t.Line}: unexpected '{t.Text}' inside {type}");
                p++;
            }
        }

        if (toks[p].Kind == T.RBrace)
        {
            p++;
        }
        else
        {
            warnings.Add($"line {toks[p].Line}: missing '}}' for {type}");
        }

        output[rootIndex] = MakeTile(depth, type, attrs, false);
        output.AddRange(children);
    }

    private static DclTile MakeTile(int depth, string type, SortedDictionary<string, string> attrs, bool isRef)
    {
        attrs.TryGetValue("key", out var key);
        attrs.TryGetValue("label", out var label);
        attrs.TryGetValue("value", out var def);
        var list = attrs.TryGetValue("list", out var l)
            ? l.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray()
            : [];
        return new DclTile(depth, type, key, label, def, list, attrs, isRef);
    }
}
