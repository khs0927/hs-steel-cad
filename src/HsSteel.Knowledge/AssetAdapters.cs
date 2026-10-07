using System.Globalization;
using System.Text.Json;
using HsSteel.Assets.Blocks;
using HsSteel.Assets.Support;

namespace HsSteel.Knowledge;

/// <summary>Maps the HsSteel.Assets readers (blocks, support files) and the doc chunker output into ingest DTOs.</summary>
public static class AssetAdapters
{
    /// <summary>Path relative to the legacy root, '/'-separated (manifest relPath format).</summary>
    public static string RelPath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    public static IReadOnlyList<BlockRecord> Blocks(string root, string blockDir, bool previews = true)
    {
        var list = new List<BlockRecord>();
        foreach (var a in BlockCatalog.Load(blockDir))
        {
            string? svg = null;
            if (previews)
            {
                try { svg = BlockPreview.RenderSvg(a, 128); }
                catch (Exception e) when (e is not OutOfMemoryException) { svg = null; }
            }

            var ext = a.Extents.Length >= 4 ? a.Extents : [0, 0, 0, 0];
            var bp = a.BasePoint.Length >= 3 ? a.BasePoint : [0, 0, 0];
            list.Add(new BlockRecord(
                a.Name, RelPath(root, a.SourcePath), bp[0], bp[1], bp[2], ext[0], ext[1], ext[2], ext[3], a.EntityCount, svg,
                a.Attributes.Select(x => new BlockAttributeRecord(x.Tag, x.Prompt, x.Default)).ToList(),
                a.Layers, a.Sha256, a.Description));
        }

        return list;
    }

    /// <summary>Support assets prefixed with <paramref name="supportRel"/> (e.g. "HSSTEEL/support") so source paths match the manifest.</summary>
    public sealed record SupportRecords(
        IReadOnlyList<PaletteItemRecord> PaletteItems,
        IReadOnlyList<CommandRecord> Commands,
        IReadOnlyList<CommandAliasRecord> Aliases,
        IReadOnlyList<LinetypeRecord> Linetypes,
        IReadOnlyList<MlineStyleRecord> MlineStyles,
        IReadOnlyList<FontMapRecord> FontMaps,
        IReadOnlyList<string> Warnings);

    public static SupportRecords Support(string supportDir, string supportRel)
    {
        var s = SupportCatalog.Load(supportDir);
        string P(string rel) => supportRel.TrimEnd('/') + "/" + rel;
        return new SupportRecords(
            s.PaletteItems.Select(p => p.BlockName is { } bn
                ? new PaletteItemRecord(p.Palette, p.Name, "block", bn, P(p.SourcePath), p.BlockSourceFile)
                : p.Command is { } cmd
                    ? new PaletteItemRecord(p.Palette, p.Name, "command", cmd, P(p.SourcePath))
                    : new PaletteItemRecord(p.Palette, p.Name, "none", null, P(p.SourcePath))).ToList(),
            s.Commands.Select(c => new CommandRecord(c.Name, c.Label, c.Macro, P(c.SourcePath), c.LispFunction)).ToList(),
            s.Aliases.Select(a => new CommandAliasRecord(a.Alias, a.Command, P(a.SourcePath))).ToList(),
            s.Linetypes.Select(l => new LinetypeRecord(l.Name, l.Description, l.Pattern, P(l.SourcePath))).ToList(),
            s.MlineStyles.Select(m => new MlineStyleRecord(m.Name, m.Raw, P(m.SourcePath))).ToList(),
            s.FontMaps.Select(f => new FontMapRecord(f.From, f.To, P(f.SourcePath))).ToList(),
            s.Warnings);
    }

    /// <summary>Reads chunks.jsonl ({id,source,sha256,page_or_sheet,kind,text}); returns empty when the file is absent.</summary>
    public static IReadOnlyList<DocChunkRecord> DocChunks(string jsonlPath)
    {
        var list = new List<DocChunkRecord>();
        if (!File.Exists(jsonlPath))
        {
            return list;
        }

        foreach (var line in File.ReadLines(jsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            using (doc)
            {
                var r = doc.RootElement;
                string? Str(string n) => r.TryGetProperty(n, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;
                var id = Str("id");
                var src = Str("source");
                var text = Str("text");
                if (id is null || src is null || text is null)
                {
                    continue;
                }

                var pos = Str("page_or_sheet");
                var page = int.TryParse(pos, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pg) ? pg : 0;
                list.Add(new DocChunkRecord(id, src.Replace('\\', '/'), page, text, pos, Str("kind"), Str("sha256")));
            }
        }

        return list;
    }
}
