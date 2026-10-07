using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HsSteel.Assets.Support;

/// <summary>One tool of a tool palette (or a toolbar/ribbon/menu entry of the CUIX).</summary>
public sealed record PaletteItem(
    string Palette, string Name, string? BlockName, string? BlockSourceFile, string? Command, string? ImageFile, string SourcePath);

/// <summary>A CUIX MenuMacro.</summary>
public sealed record CommandDef(
    string Id, string Name, string? Label, string? Macro, string? LispFunction, string? Image, string SourcePath);

/// <summary>A PGP alias (alias -> command).</summary>
public sealed record CommandAlias(string Alias, string Command, string SourcePath);

/// <summary>A .lin linetype definition.</summary>
public sealed record LinetypeDef(string Name, string Description, string Pattern, string SourcePath);

/// <summary>A .mln multiline style (raw DXF-like group code/value text).</summary>
public sealed record MlineStyleDef(string Name, string Raw, string SourcePath);

/// <summary>A .fmp font mapping (From;To).</summary>
public sealed record FontMap(string From, string To, string SourcePath);

/// <summary>Everything parsed from an HS-STEEL support directory.</summary>
public sealed record SupportAssets(
    IReadOnlyList<PaletteItem> PaletteItems,
    IReadOnlyList<CommandDef> Commands,
    IReadOnlyList<CommandAlias> Aliases,
    IReadOnlyList<LinetypeDef> Linetypes,
    IReadOnlyList<MlineStyleDef> MlineStyles,
    IReadOnlyList<FontMap> FontMaps,
    IReadOnlyList<string> Warnings);

/// <summary>Loads HS-STEEL legacy support files (*.atc/*.xtp/*.cuix/*.pgp/*.lin/*.mln/*.fmp). Output is deterministic.</summary>
public static partial class SupportCatalog
{
    static SupportCatalog() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static SupportAssets Load(string supportDir)
    {
        var warnings = new List<string>();
        var palettes = new List<PaletteItem>();
        var commands = new List<CommandDef>();
        var aliases = new List<CommandAlias>();
        var linetypes = new List<LinetypeDef>();
        var mlines = new List<MlineStyleDef>();
        var fonts = new List<FontMap>();

        if (!Directory.Exists(supportDir))
        {
            warnings.Add($"Support directory not found: {supportDir}");
            return Build(palettes, commands, aliases, linetypes, mlines, fonts, warnings);
        }

        var root = Path.GetFullPath(supportDir);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Rel(root, f)))
            .OrderBy(f => f.Rel, StringComparer.Ordinal)
            .ToList();

        // Palettes: *.atc first, then *.xtp; de-duplicated by palette id.
        var seenPalettes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paletteFiles = files
            .Where(f => f.Rel.EndsWith(".atc", StringComparison.OrdinalIgnoreCase) || f.Rel.EndsWith(".xtp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Rel.EndsWith(".xtp", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(f => f.Rel, StringComparer.Ordinal);
        foreach (var f in paletteFiles)
        {
            Guard(warnings, f.Rel, () => ParsePaletteFile(f.Full, f.Rel, seenPalettes, palettes));
        }

        foreach (var f in files)
        {
            var ext = Path.GetExtension(f.Rel).ToLowerInvariant();
            switch (ext)
            {
                case ".cuix" when !f.Rel.Contains(".bak.", StringComparison.OrdinalIgnoreCase):
                    Guard(warnings, f.Rel, () => ParseCuix(f.Full, f.Rel, palettes, commands, warnings));
                    break;
                case ".pgp":
                    Guard(warnings, f.Rel, () => ParsePgp(ReadText(f.Full), f.Rel, aliases));
                    break;
                case ".lin":
                    Guard(warnings, f.Rel, () => ParseLin(ReadText(f.Full), f.Rel, linetypes));
                    break;
                case ".mln":
                    Guard(warnings, f.Rel, () => ParseMln(ReadText(f.Full), f.Rel, mlines));
                    break;
                case ".fmp":
                    Guard(warnings, f.Rel, () => ParseFmp(ReadText(f.Full), f.Rel, fonts));
                    break;
            }
        }

        return Build(palettes, commands, aliases, linetypes, mlines, fonts, warnings);
    }

    private static SupportAssets Build(
        List<PaletteItem> palettes, List<CommandDef> commands, List<CommandAlias> aliases, List<LinetypeDef> linetypes,
        List<MlineStyleDef> mlines, List<FontMap> fonts, List<string> warnings)
    {
        var o = StringComparer.Ordinal;
        return new SupportAssets(
            palettes.Select((p, i) => (p, i)).OrderBy(x => x.p.Palette, o).ThenBy(x => x.i).Select(x => x.p).ToList(),
            commands.OrderBy(c => c.Id, o).ThenBy(c => c.SourcePath, o).ToList(),
            aliases.OrderBy(a => a.Alias, o).ThenBy(a => a.Command, o).ThenBy(a => a.SourcePath, o).ToList(),
            linetypes.OrderBy(l => l.Name, o).ThenBy(l => l.SourcePath, o).ToList(),
            mlines.OrderBy(m => m.Name, o).ThenBy(m => m.SourcePath, o).ToList(),
            fonts.OrderBy(f => f.From, o).ThenBy(f => f.To, o).ThenBy(f => f.SourcePath, o).ToList(),
            warnings.Distinct(o).OrderBy(w => w, o).ToList());
    }

    private static void Guard(List<string> warnings, string rel, Action a)
    {
        try
        {
            a();
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or InvalidDataException or UnauthorizedAccessException)
        {
            warnings.Add($"{rel}: {e.GetType().Name}: {e.Message}");
        }
    }

    private static string Rel(string root, string full) =>
        Path.GetRelativePath(root, full).Replace('\\', '/');

    /// <summary>Reads text: BOM-aware, strict UTF-8 first, CP949 fallback.</summary>
    internal static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(949).GetString(bytes);
        }
    }

    // ---------------------------------------------------------------- palettes

    private static void ParsePaletteFile(string full, string rel, HashSet<string> seen, List<PaletteItem> items)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(ReadText(full));
        }
        catch (System.Xml.XmlException)
        {
            return; // not a palette file
        }

        var fileName = Path.GetFileNameWithoutExtension(rel);
        foreach (var pal in doc.Root!.DescendantsAndSelf("Palette").Where(p => p.Element("Tools") != null))
        {
            var id = pal.Element("ItemID")?.Attribute("idValue")?.Value ?? rel;
            if (!seen.Add(id))
            {
                continue;
            }

            var name = pal.Element("Properties")?.Element("ItemName")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                var us = fileName.LastIndexOf('_');
                name = us > 0 ? fileName[..us] : fileName;
            }

            foreach (var tool in pal.Element("Tools")!.Elements("Tool"))
            {
                var props = tool.Element("Properties");
                var data = tool.Element("Data");
                var block = data?.Element("Block");
                var src = block?.Element("SourceFile")?.Value?.Trim();
                var blockName = block?.Element("BlockName")?.Value?.Trim();
                if (string.IsNullOrEmpty(blockName) && !string.IsNullOrEmpty(src))
                {
                    blockName = FileStem(src);
                }

                var macro = data?.Element("Command")?.Element("Macro")?.Value?.Trim();
                var img = props?.Element("Images")?.Elements("Image").Select(i => i.Attribute("src")?.Value).FirstOrDefault(s => !string.IsNullOrEmpty(s));
                if (img != null)
                {
                    img = img.Replace('\\', '/');
                    if (img.StartsWith("./", StringComparison.Ordinal))
                    {
                        img = img[2..];
                    }
                }

                items.Add(new PaletteItem(
                    name!, props?.Element("ItemName")?.Value?.Trim() ?? string.Empty,
                    NullIfEmpty(blockName), NullIfEmpty(src), NullIfEmpty(macro), NullIfEmpty(img), rel));
            }
        }
    }

    private static string FileStem(string path)
    {
        var s = path.Replace('\\', '/');
        s = s[(s.LastIndexOf('/') + 1)..];
        var dot = s.LastIndexOf('.');
        return dot > 0 ? s[..dot] : s;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // ---------------------------------------------------------------- cuix

    [GeneratedRegex(@"(?<!\$)\(\s*([A-Za-z_][\w\-*:!.]*)")]
    private static partial Regex LispCall();

    /// <summary>Extracts the first Lisp function call from a menu macro, e.g. "^C^C(hs-foo 1)" gives "hs-foo".</summary>
    public static string? ExtractLispFunction(string? macro)
    {
        if (string.IsNullOrEmpty(macro))
        {
            return null;
        }

        var m = LispCall().Match(macro);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static void ParseCuix(string full, string rel, List<PaletteItem> items, List<CommandDef> commands, List<string> warnings)
    {
        using var zip = ZipFile.OpenRead(full);
        XDocument? Load(string name)
        {
            var e = zip.Entries.FirstOrDefault(x => string.Equals(x.FullName, name, StringComparison.OrdinalIgnoreCase));
            if (e == null)
            {
                return null;
            }

            using var s = e.Open();
            return XDocument.Load(s);
        }

        var macros = new Dictionary<string, CommandDef>(StringComparer.Ordinal);
        var menu = Load("MenuGroup.cui");
        if (menu?.Root == null)
        {
            warnings.Add($"{rel}: MenuGroup.cui missing");
            return;
        }

        foreach (var mm in menu.Root.Descendants("MenuMacro"))
        {
            var id = mm.Attribute("UID")?.Value;
            var m = mm.Element("Macro");
            if (id == null || m == null)
            {
                continue;
            }

            var cmd = NullIfEmpty(m.Element("Command")?.Value?.Trim());
            var name = m.Element("Name")?.Value?.Trim() ?? id;
            var def = new CommandDef(
                id, name, NullIfEmpty(m.Element("HelpString")?.Value?.Trim()), cmd, ExtractLispFunction(cmd),
                NullIfEmpty(m.Element("SmallImage")?.Attribute("Name")?.Value), rel + "!MenuGroup.cui");
            if (macros.TryAdd(id, def))
            {
                commands.Add(def);
            }
            else
            {
                warnings.Add($"{rel}: duplicate MenuMacro UID {id}");
            }
        }

        var unresolved = new HashSet<string>(StringComparer.Ordinal);

        void Add(string palette, string name, string? macroId, string part)
        {
            if (macroId == null)
            {
                return;
            }

            macros.TryGetValue(macroId, out var def);
            if (def == null)
            {
                unresolved.Add(macroId);
            }

            items.Add(new PaletteItem(palette, name, null, null, def?.Macro ?? macroId, def?.Image, rel + "!" + part));
        }

        var tb = Load("ToolbarRoot.cui");
        if (tb?.Root != null)
        {
            foreach (var t in tb.Root.Descendants("Toolbar"))
            {
                var tname = t.Element("Alias")?.Value ?? t.Element("Name")?.Value ?? t.Attribute("UID")?.Value ?? "?";
                foreach (var b in t.Descendants("ToolbarButton").Where(b => b.Attribute("IsSeparator")?.Value != "true"))
                {
                    Add("toolbar:" + tname.Trim(), b.Element("Name")?.Value?.Trim() ?? string.Empty,
                        b.Element("MenuItem")?.Element("MacroRef")?.Attribute("MenuMacroID")?.Value, "ToolbarRoot.cui");
                }
            }
        }

        var rb = Load("RibbonRoot.cui");
        if (rb?.Root != null)
        {
            foreach (var b in rb.Root.Descendants().Where(e => e.Name.LocalName is "RibbonCommandButton" or "RibbonToggleButton"))
            {
                var id = b.Attribute("MenuMacroID")?.Value;
                if (id == null)
                {
                    continue;
                }

                var panel = b.Ancestors("RibbonPanelSource").Select(p => p.Attribute("Text")?.Value).FirstOrDefault(x => !string.IsNullOrEmpty(x))
                    ?? b.Ancestors("RibbonTabSource").Select(p => p.Attribute("Text")?.Value).FirstOrDefault(x => !string.IsNullOrEmpty(x))
                    ?? "(none)";
                var text = b.Attribute("Text")?.Value;
                if (string.IsNullOrEmpty(text) && macros.TryGetValue(id, out var d))
                {
                    text = d.Name;
                }

                Add("ribbon:" + panel, text ?? id, id, "RibbonRoot.cui");
            }
        }

        var pm = Load("PopMenuRoot.cui");
        if (pm?.Root != null)
        {
            foreach (var p in pm.Root.Descendants("PopMenu"))
            {
                var pname = p.Element("Alias")?.Value ?? p.Element("Name")?.Value ?? p.Attribute("UID")?.Value ?? "?";
                foreach (var i in p.Elements("PopMenuItem").Where(i => i.Attribute("IsSeparator")?.Value != "true"))
                {
                    Add("menu:" + pname.Trim(), i.Element("NameRef")?.Value?.Trim() ?? string.Empty,
                        i.Element("MenuItem")?.Element("MacroRef")?.Attribute("MenuMacroID")?.Value, "PopMenuRoot.cui");
                }
            }
        }

        if (unresolved.Count > 0)
        {
            warnings.Add($"{rel}: {unresolved.Count} MenuMacroID references do not resolve (e.g. {unresolved.OrderBy(x => x, StringComparer.Ordinal).First()})");
        }
    }

    // ---------------------------------------------------------------- text formats

    private static IEnumerable<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static void ParsePgp(string text, string rel, List<CommandAlias> aliases)
    {
        foreach (var raw in Lines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';')
            {
                continue;
            }

            var comma = line.IndexOf(',');
            if (comma <= 0)
            {
                continue;
            }

            var alias = line[..comma].Trim();
            var cmd = line[(comma + 1)..].Trim();
            var semi = cmd.IndexOf(';');
            if (semi >= 0)
            {
                cmd = cmd[..semi].Trim();
            }

            cmd = cmd.TrimStart('*').Trim();
            if (alias.Length > 0 && cmd.Length > 0)
            {
                aliases.Add(new CommandAlias(alias, cmd, rel));
            }
        }
    }

    private static void ParseLin(string text, string rel, List<LinetypeDef> list)
    {
        string? name = null, desc = null;
        foreach (var raw in Lines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';')
            {
                continue;
            }

            if (line[0] == '*')
            {
                var comma = line.IndexOf(',');
                name = (comma > 0 ? line[1..comma] : line[1..]).Trim();
                desc = comma > 0 ? line[(comma + 1)..].Trim() : string.Empty;
            }
            else if (name != null)
            {
                list.Add(new LinetypeDef(name, desc ?? string.Empty, line, rel));
                name = null;
            }
        }
    }

    private static void ParseMln(string text, string rel, List<MlineStyleDef> list)
    {
        var lines = Lines(text).ToList();
        var starts = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals("MLSTYLE", StringComparison.OrdinalIgnoreCase))
            {
                starts.Add(i);
            }
        }

        for (var s = 0; s < starts.Count; s++)
        {
            var from = starts[s] + 1;
            var to = s + 1 < starts.Count ? starts[s + 1] : lines.Count; // exclusive
            var block = lines.Skip(from).Take(to - from).Select(l => l.TrimEnd()).ToList();
            while (block.Count > 0 && (block[^1].Trim().Length == 0 || block[^1].Trim() == "0"))
            {
                block.RemoveAt(block.Count - 1);
            }

            string? name = null;
            for (var i = 0; i + 1 < block.Count; i += 2)
            {
                if (block[i].Trim() == "2")
                {
                    name = block[i + 1].Trim();
                    break;
                }
            }

            if (name != null)
            {
                list.Add(new MlineStyleDef(name, string.Join("\n", block.Select(b => b.Trim())), rel));
            }
        }
    }

    private static void ParseFmp(string text, string rel, List<FontMap> list)
    {
        foreach (var raw in Lines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';')
            {
                continue;
            }

            var semi = line.IndexOf(';');
            if (semi <= 0)
            {
                continue;
            }

            var from = line[..semi].Trim();
            var to = line[(semi + 1)..].Trim();
            if (from.Length > 0 && to.Length > 0)
            {
                list.Add(new FontMap(from, to, rel));
            }
        }
    }
}
