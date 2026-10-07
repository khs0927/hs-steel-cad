using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HsSteel.Assets;
using Microsoft.Data.Sqlite;

namespace HsSteel.Knowledge;

/// <summary>
/// Builds hs_assets.db deterministically: everything is collected in memory, sorted ordinally and
/// written in one transaction; no timestamps; ids are assigned from sorted order; VACUUM at the end.
/// </summary>
public sealed class KnowledgeDbBuilder
{
    public const string Schema = """
        CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE source_file(id INTEGER PRIMARY KEY, rel_path TEXT NOT NULL UNIQUE, size INTEGER NOT NULL, sha256 TEXT NOT NULL,
            disposition TEXT NOT NULL, reason TEXT NOT NULL, target TEXT NOT NULL);
        CREATE TABLE section(id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE, family TEXT NOT NULL, spec TEXT NOT NULL, spec_norm TEXT NOT NULL,
            shape TEXT NOT NULL, m2 REAL, m3 REAL, m4 REAL, m5 REAL, m6 REAL, m7 REAL, unit_weight REAL NOT NULL, paint_area REAL,
            color INTEGER, family_label TEXT, raw_line TEXT NOT NULL, source_file_id INTEGER REFERENCES source_file(id));
        CREATE INDEX ix_section_norm ON section(spec_norm);
        CREATE TABLE bolt(id INTEGER PRIMARY KEY, spec TEXT NOT NULL UNIQUE, grade TEXT NOT NULL, diameter INTEGER NOT NULL, length INTEGER NOT NULL,
            source_file_id INTEGER REFERENCES source_file(id));
        CREATE TABLE section_alias(id INTEGER PRIMARY KEY, target_kind TEXT NOT NULL, target_key TEXT NOT NULL, section_id INTEGER REFERENCES section(id),
            alias TEXT NOT NULL, alias_norm TEXT NOT NULL, lang TEXT NOT NULL, priority INTEGER NOT NULL);
        CREATE INDEX ix_alias_norm ON section_alias(alias_norm);
        CREATE TABLE block(id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE, source_file_id INTEGER REFERENCES source_file(id),
            base_x REAL, base_y REAL, base_z REAL, ext_minx REAL, ext_miny REAL, ext_maxx REAL, ext_maxy REAL, entity_count INTEGER, preview_svg TEXT, sha256 TEXT, description TEXT);
        CREATE TABLE block_attribute(block_id INTEGER NOT NULL REFERENCES block(id), tag TEXT NOT NULL, prompt TEXT, default_value TEXT);
        CREATE TABLE block_layer(block_id INTEGER NOT NULL REFERENCES block(id), layer TEXT NOT NULL);
        CREATE TABLE palette_item(id INTEGER PRIMARY KEY, palette TEXT NOT NULL, name TEXT NOT NULL, target_kind TEXT NOT NULL, target TEXT,
            source_file_id INTEGER REFERENCES source_file(id));
        CREATE TABLE command(id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE, description TEXT, macro TEXT, source_file_id INTEGER REFERENCES source_file(id), lisp_function TEXT);
        CREATE TABLE command_alias(alias TEXT NOT NULL, command TEXT NOT NULL, source_file_id INTEGER REFERENCES source_file(id));
        CREATE TABLE linetype(id INTEGER PRIMARY KEY, name TEXT NOT NULL, description TEXT, definition TEXT NOT NULL, source_file_id INTEGER REFERENCES source_file(id));
        CREATE TABLE mline_style(id INTEGER PRIMARY KEY, name TEXT NOT NULL, raw TEXT NOT NULL, source_file_id INTEGER REFERENCES source_file(id));
        CREATE TABLE font_map(id INTEGER PRIMARY KEY, from_font TEXT NOT NULL, to_font TEXT NOT NULL, source_file_id INTEGER REFERENCES source_file(id));
        CREATE TABLE doc_chunk(id TEXT PRIMARY KEY, source_file_id INTEGER REFERENCES source_file(id), page INTEGER NOT NULL, text TEXT NOT NULL,
            page_or_sheet TEXT, kind TEXT, sha256 TEXT);
        CREATE TABLE node(id INTEGER PRIMARY KEY, kind TEXT NOT NULL, key TEXT NOT NULL, label TEXT NOT NULL, props_json TEXT NOT NULL, UNIQUE(kind, key));
        CREATE TABLE edge(src INTEGER NOT NULL REFERENCES node(id), rel TEXT NOT NULL, dst INTEGER NOT NULL REFERENCES node(id),
            evidence_source_file TEXT, evidence_note TEXT);
        CREATE INDEX ix_edge_src ON edge(src, rel);
        CREATE INDEX ix_edge_dst ON edge(dst, rel);
        CREATE VIRTUAL TABLE search_fts USING fts5(kind UNINDEXED, key UNINDEXED, label, body, tokenize = "unicode61 remove_diacritics 0 tokenchars '.'");
        """;

    private sealed record SectionRow(string Key, string Family, SectionRecord Rec, string RawLine, string SourceRel);
    private sealed record BoltRow(string Spec, string Grade, int D, int L, string SourceRel);
    private sealed record SpliceRow(string SectionSpec, string BoltSpec, string SourceRel, string Note);

    private readonly Manifest manifest;
    private readonly List<SectionRow> sections = [];
    private readonly Dictionary<string, BoltRow> bolts = new(StringComparer.Ordinal);
    private readonly List<SpliceRow> splices = [];
    private readonly List<BlockRecord> blocks = [];
    private readonly List<PaletteItemRecord> palette = [];
    private readonly List<CommandRecord> commands = [];
    private readonly List<CommandAliasRecord> commandAliases = [];
    private readonly List<LinetypeRecord> linetypes = [];
    private readonly List<MlineStyleRecord> mlineStyles = [];
    private readonly List<FontMapRecord> fontMaps = [];
    private readonly List<DocChunkRecord> docChunks = [];

    public KnowledgeDbBuilder(Manifest manifest) => this.manifest = manifest;

    /// <summary>Ingests every manifest row with target "sections" (and bolts/splices from "connections") under <paramref name="root"/>.</summary>
    public KnowledgeDbBuilder IngestSections(string root)
    {
        foreach (var f in manifest.Files.Where(f => f.Disposition == ManifestScanner.Ingest && f.Target == "sections"))
        {
            var family = Path.GetFileNameWithoutExtension(f.RelPath);
            var text = File.ReadAllText(Path.Combine(root, f.RelPath), SectionTable.Cp949);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var raw in text.Split('\n').Skip(1))
            {
                var line = raw.TrimEnd('\r');
                var rec = SectionTable.Parse("\n" + line, family).FirstOrDefault();
                if (rec is null)
                {
                    continue;
                }

                var key = family + "/" + rec.Spec;
                var n = seen.TryGetValue(key, out var c) ? c + 1 : 1;
                seen[key] = n;
                sections.Add(new SectionRow(n == 1 ? key : key + "#" + n.ToString(CultureInfo.InvariantCulture), family, rec, line, f.RelPath));
            }
        }

        var quoted = new Regex("\"([^\"]*)\"", RegexOptions.CultureInvariant);
        foreach (var f in manifest.Files.Where(f => f.Disposition == ManifestScanner.Ingest && f.Target == "connections"))
        {
            var table = Path.GetFileNameWithoutExtension(f.RelPath);
            var text = File.ReadAllText(Path.Combine(root, f.RelPath), SectionTable.Cp949);
            foreach (var raw in text.Split('\n'))
            {
                var q = quoted.Matches(raw).Select(m => m.Groups[1].Value).ToList();
                if (q.Count < 2)
                {
                    continue;
                }

                foreach (var b in q.Skip(1).Distinct(StringComparer.Ordinal))
                {
                    if (SpecAliases.TryParseBolt(b, out var grade, out var d, out var l))
                    {
                        bolts.TryAdd(b, new BoltRow(b, grade, d, l, f.RelPath));
                        splices.Add(new SpliceRow(q[0], b, f.RelPath, table + " splice"));
                    }
                }
            }
        }

        return this;
    }

    public KnowledgeDbBuilder IngestBlocks(IEnumerable<BlockRecord> items) { blocks.AddRange(items); return this; }

    public KnowledgeDbBuilder IngestPaletteItems(IEnumerable<PaletteItemRecord> items) { palette.AddRange(items); return this; }

    public KnowledgeDbBuilder IngestCommands(IEnumerable<CommandRecord> items) { commands.AddRange(items); return this; }

    public KnowledgeDbBuilder IngestCommandAliases(IEnumerable<CommandAliasRecord> items) { commandAliases.AddRange(items); return this; }

    public KnowledgeDbBuilder IngestLinetypes(IEnumerable<LinetypeRecord> items) { linetypes.AddRange(items); return this; }

    public KnowledgeDbBuilder IngestMlineStyles(IEnumerable<MlineStyleRecord> items) { mlineStyles.AddRange(items); return this; }

    public KnowledgeDbBuilder IngestFontMaps(IEnumerable<FontMapRecord> items) { fontMaps.AddRange(items); return this; }

    public KnowledgeDbBuilder IngestDocChunks(IEnumerable<DocChunkRecord> items) { docChunks.AddRange(items); return this; }

    public static string ConnectionString(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString();

    public void Build(string dbPath)
    {
        var full = Path.GetFullPath(dbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.Delete(full);

        using var cn = new SqliteConnection(ConnectionString(full, SqliteOpenMode.ReadWriteCreate));
        cn.Open();
        Exec(cn, "PRAGMA page_size=4096; PRAGMA journal_mode=DELETE;");
        Exec(cn, Schema);
        using (var tx = cn.BeginTransaction())
        {
            Write(cn, tx);
            tx.Commit();
        }

        Exec(cn, "INSERT INTO search_fts(search_fts) VALUES('optimize');");
        Exec(cn, "VACUUM;");
    }

    private void Write(SqliteConnection cn, SqliteTransaction tx)
    {
        var o = StringComparer.Ordinal;
        Insert(cn, tx, "INSERT INTO meta VALUES($a,$b)", [["schema", "hs-assets/1"], ["manifest_schema", manifest.Schema], ["manifest_file_count", manifest.FileCount]]);

        // source_file: manifest order (already ordinal by relPath); id = 1-based index
        var files = manifest.Files.OrderBy(f => f.RelPath, o).ToList();
        var fileId = new Dictionary<string, long>(o);
        for (var i = 0; i < files.Count; i++)
        {
            fileId[files[i].RelPath] = i + 1;
        }

        Insert(cn, tx, "INSERT INTO source_file VALUES($a,$b,$c,$d,$e,$f,$g)",
            files.Select((f, i) => new object?[] { i + 1, f.RelPath, f.Size, f.Sha256, f.Disposition, f.Reason, f.Target }));
        object? Fid(string rel) => fileId.TryGetValue(rel.Split('!')[0].Replace('\\', '/'), out var id) ? id : null;

        var nodes = new SortedDictionary<(string Kind, string Key), (string Label, string Props)>(Comparer<(string, string)>.Create((a, b) =>
        {
            var c = o.Compare(a.Item1, b.Item1);
            return c != 0 ? c : o.Compare(a.Item2, b.Item2);
        }));
        var edges = new List<(string SK, string SKey, string Rel, string DK, string DKey, string? Ev, string? Note)>();
        var fts = new List<(string Kind, string Key, string Label, string Body)>();
        var aliases = new List<(string Kind, string Key, long? SectionId, AliasRow A)>();

        // sections
        var secs = sections.OrderBy(s => s.Key, o).ToList();
        var secId = new Dictionary<string, long>(o);
        var secBySpec = new Dictionary<string, string>(o);
        Insert(cn, tx, "INSERT INTO section VALUES($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k,$l,$m,$n,$o,$p,$q,$r)", secs.Select((s, i) =>
        {
            secId[s.Key] = i + 1;
            secBySpec.TryAdd(s.Rec.Spec, s.Key);
            var m = s.Rec.M;
            return new object?[]
            {
                i + 1, s.Key, s.Family, s.Rec.Spec, SpecAliases.Normalize(s.Rec.Spec), s.Rec.Shape,
                m[0], m[1], m[2], m[3], m[4], m[5], s.Rec.UnitWeight, s.Rec.PaintArea, s.Rec.Color, s.Rec.Family, s.RawLine, Fid(s.SourceRel),
            };
        }).ToList());

        foreach (var fam in secs.Select(s => s.Family).Distinct(o))
        {
            nodes[("family", fam)] = (fam, Json(new SortedDictionary<string, object?> { ["korean"] = SpecAliases.KoreanFor(fam) }));
        }

        foreach (var s in secs)
        {
            var al = SpecAliases.ForSection(s.Rec.Spec, s.Family);
            aliases.AddRange(al.Select(a => ("section", s.Key, (long?)secId[s.Key], a)));
            nodes[("section", s.Key)] = (s.Rec.Spec, Json(new SortedDictionary<string, object?>
            {
                ["family"] = s.Family, ["shape"] = s.Rec.Shape, ["dims"] = s.Rec.M, ["unit_weight"] = s.Rec.UnitWeight, ["paint_area"] = s.Rec.PaintArea,
            }));
            edges.Add(("section", s.Key, "family", "family", s.Family, s.SourceRel, "section row of family table " + s.Family));
            fts.Add(("section", s.Key, s.Rec.Spec, string.Join(' ', al.Select(a => a.Alias).Append(s.Family).Append(s.Rec.Family))));
        }

        // bolts
        var bl = bolts.Values.OrderBy(b => b.Spec, o).ToList();
        Insert(cn, tx, "INSERT INTO bolt VALUES($a,$b,$c,$d,$e,$f)", bl.Select((b, i) => new object?[] { i + 1, b.Spec, b.Grade, b.D, b.L, Fid(b.SourceRel) }));
        foreach (var b in bl)
        {
            var al = SpecAliases.ForBolt(b.Spec);
            aliases.AddRange(al.Select(a => ("bolt", b.Spec, (long?)null, a)));
            nodes[("bolt", b.Spec)] = (b.Spec, Json(new SortedDictionary<string, object?> { ["grade"] = b.Grade, ["diameter"] = b.D, ["length"] = b.L }));
            fts.Add(("bolt", b.Spec, b.Spec, string.Join(' ', al.Select(a => a.Alias))));
        }

        foreach (var sp in splices.DistinctBy(x => (x.SectionSpec, x.BoltSpec, x.SourceRel)))
        {
            if (secBySpec.TryGetValue(sp.SectionSpec, out var sk))
            {
                edges.Add(("section", sk, "spliced_with", "bolt", sp.BoltSpec, sp.SourceRel, sp.Note));
            }
        }

        var aliasRows = aliases.OrderBy(a => a.Kind, o).ThenBy(a => a.Key, o).ThenBy(a => a.A.Priority).ThenBy(a => a.A.Alias, o).ToList();
        Insert(cn, tx, "INSERT INTO section_alias VALUES($a,$b,$c,$d,$e,$f,$g,$h)",
            aliasRows.Select((a, i) => new object?[] { i + 1, a.Kind, a.Key, a.SectionId, a.A.Alias, SpecAliases.Normalize(a.A.Alias), a.A.Lang, a.A.Priority }));

        // blocks
        var bks = blocks.OrderBy(b => b.Name, o).ThenBy(b => b.SourceRelPath, o).DistinctBy(b => b.Name).ToList();
        var blockByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in bks)
        {
            blockByName.TryAdd(b.Name, b.Name);
        }

        Insert(cn, tx, "INSERT INTO block VALUES($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k,$l,$m,$n)", bks.Select((b, i) => new object?[]
            { i + 1, b.Name, Fid(b.SourceRelPath), b.BaseX, b.BaseY, b.BaseZ, b.MinX, b.MinY, b.MaxX, b.MaxY, b.EntityCount, b.PreviewSvg, b.Sha256, b.Description }));
        Insert(cn, tx, "INSERT INTO block_attribute VALUES($a,$b,$c,$d)", bks.SelectMany((b, i) =>
            b.Attributes.OrderBy(a => a.Tag, o).Select(a => new object?[] { i + 1, a.Tag, a.Prompt, a.Default })));
        Insert(cn, tx, "INSERT INTO block_layer VALUES($a,$b)", bks.SelectMany((b, i) =>
            b.Layers.Distinct(o).OrderBy(l => l, o).Select(l => new object?[] { i + 1, l })));
        var blockExtra = new Dictionary<string, List<string>>(o); // search text contributed by evidence-linked palette items / commands
        foreach (var b in bks)
        {
            nodes[("block", b.Name)] = (b.Name, Json(new SortedDictionary<string, object?>
                { ["entity_count"] = b.EntityCount, ["attributes"] = b.Attributes.Select(a => a.Tag).OrderBy(t => t, o).ToArray(), ["description"] = b.Description }));
            foreach (var l in b.Layers.Distinct(o))
            {
                nodes.TryAdd(("layer", l), (l, "{}"));
                edges.Add(("block", b.Name, "on_layer", "layer", l, b.SourceRelPath, "block drawing entities on layer"));
            }
        }

        // commands
        var cmds = commands.OrderBy(c => c.Name, o).ThenBy(c => c.SourceRelPath, o).ThenBy(c => c.Macro, o).DistinctBy(c => c.Name).ToList();
        Insert(cn, tx, "INSERT INTO command VALUES($a,$b,$c,$d,$e,$f)", cmds.Select((c, i) => new object?[] { i + 1, c.Name, c.Description, c.Macro, Fid(c.SourceRelPath), c.LispFunction }));
        var cmdByName = cmds.ToDictionary(c => c.Name, o);
        var cmdsByMacro = cmds.Where(c => !string.IsNullOrEmpty(c.Macro)).GroupBy(c => c.Macro!, o).ToDictionary(g => g.Key, g => g.ToList(), o);
        foreach (var c in cmds)
        {
            nodes[("command", c.Name)] = (c.Name, Json(new SortedDictionary<string, object?> { ["description"] = c.Description, ["macro"] = c.Macro, ["lisp_function"] = c.LispFunction }));
            if (!string.IsNullOrEmpty(c.LispFunction))
            {
                nodes.TryAdd(("lisp", c.LispFunction), (c.LispFunction, "{}"));
                edges.Add(("command", c.Name, "calls_lisp", "lisp", c.LispFunction, c.SourceRelPath, "menu macro calls (" + c.LispFunction + " ...)"));
            }

            foreach (var bn in BlockRefs(c.Macro, bks))
            {
                edges.Add(("command", c.Name, "uses_block", "block", bn, c.SourceRelPath, "macro text references block name: " + Clip(c.Macro!)));
                AddExtra(blockExtra, bn, c.Name + " " + c.Description);
            }
        }

        var cal = commandAliases.OrderBy(a => a.Alias, o).ThenBy(a => a.Command, o).ThenBy(a => a.SourceRelPath, o).ToList();
        Insert(cn, tx, "INSERT INTO command_alias VALUES($a,$b,$c)", cal.Select(a => new object?[] { a.Alias, a.Command, Fid(a.SourceRelPath) }));
        foreach (var a in cal)
        {
            nodes.TryAdd(("command", a.Command), (a.Command, "{}"));
            nodes.TryAdd(("command_alias", a.Alias), (a.Alias, "{}"));
            edges.Add(("command_alias", a.Alias, "alias_of", "command", a.Command, a.SourceRelPath, "pgp alias " + a.Alias + " -> " + a.Command));
            fts.Add(("command_alias", a.Alias, a.Alias, a.Command));
        }

        // palette
        var pal = palette.OrderBy(p => p.Palette, o).ThenBy(p => p.Name, o).ThenBy(p => p.TargetKind, o).ThenBy(p => p.Target, o).ThenBy(p => p.SourceRelPath, o).ToList();
        Insert(cn, tx, "INSERT INTO palette_item VALUES($a,$b,$c,$d,$e,$f)", pal.Select((p, i) => new object?[] { i + 1, p.Palette, p.Name, p.TargetKind, p.Target, Fid(p.SourceRelPath) }));
        var palSeen = new Dictionary<string, int>(o);
        foreach (var p in pal)
        {
            var baseKey = p.Palette + "/" + p.Name;
            var n = palSeen.TryGetValue(baseKey, out var pc) ? pc + 1 : 1;
            palSeen[baseKey] = n;
            var key = n == 1 ? baseKey : baseKey + "#" + n.ToString(CultureInfo.InvariantCulture);
            nodes[("palette_item", key)] = (p.Name, Json(new SortedDictionary<string, object?> { ["palette"] = p.Palette, ["target_kind"] = p.TargetKind, ["target"] = p.Target }));
            fts.Add(("palette_item", key, p.Name, p.Palette + " " + p.Target));
            if (string.IsNullOrEmpty(p.Target))
            {
                continue;
            }

            if (p.TargetKind == "block")
            {
                string? bn = null;
                string? how = null;
                if (blockByName.TryGetValue(p.Target, out var byName))
                {
                    bn = byName;
                    how = "palette tool block name matches block " + bn;
                }
                else if (!string.IsNullOrEmpty(p.TargetFile) && blockByName.TryGetValue(FileStem(p.TargetFile), out var byFile))
                {
                    bn = byFile;
                    how = "palette tool source file " + p.TargetFile + " is block " + bn;
                }

                if (bn is not null)
                {
                    edges.Add(("palette_item", key, "inserts", "block", bn, p.SourceRelPath, how));
                    AddExtra(blockExtra, bn, p.Name + " " + p.Palette);
                }
            }
            else if (p.TargetKind == "command")
            {
                if (cmdByName.ContainsKey(p.Target))
                {
                    edges.Add(("palette_item", key, "invokes", "command", p.Target, p.SourceRelPath, "palette tool command name"));
                }
                else if (cmdsByMacro.TryGetValue(p.Target, out var same) && same.Count == 1)
                {
                    edges.Add(("palette_item", key, "invokes", "command", same[0].Name, p.SourceRelPath, "identical macro text (unique): " + Clip(p.Target)));
                }
            }
        }

        foreach (var b in bks)
        {
            fts.Add(("block", b.Name, b.Name, string.Join(' ', b.Attributes.SelectMany(a => new[] { a.Tag, a.Prompt ?? string.Empty, a.Default ?? string.Empty })
                .Concat(b.Layers).Append(b.Description ?? string.Empty).Concat(blockExtra.TryGetValue(b.Name, out var ex) ? ex.Distinct(o).OrderBy(x => x, o) : []))));
        }

        foreach (var c in cmds)
        {
            fts.Add(("command", c.Name, c.Name, $"{c.Description} {c.Macro}"));
        }

        // linetypes
        var lts = linetypes.OrderBy(l => l.Name, o).ThenBy(l => l.SourceRelPath, o).ThenBy(l => l.Definition, o).ToList();
        Insert(cn, tx, "INSERT INTO linetype VALUES($a,$b,$c,$d,$e)", lts.Select((l, i) => new object?[] { i + 1, l.Name, l.Description, l.Definition, Fid(l.SourceRelPath) }));
        foreach (var l in lts)
        {
            nodes.TryAdd(("linetype", l.Name), (l.Name, Json(new SortedDictionary<string, object?> { ["description"] = l.Description })));
        }

        var mls = mlineStyles.OrderBy(m => m.Name, o).ThenBy(m => m.SourceRelPath, o).ThenBy(m => m.Raw, o).ToList();
        Insert(cn, tx, "INSERT INTO mline_style VALUES($a,$b,$c,$d)", mls.Select((m, i) => new object?[] { i + 1, m.Name, m.Raw, Fid(m.SourceRelPath) }));
        var fms = fontMaps.OrderBy(f => f.From, o).ThenBy(f => f.To, o).ThenBy(f => f.SourceRelPath, o).ToList();
        Insert(cn, tx, "INSERT INTO font_map VALUES($a,$b,$c,$d)", fms.Select((f, i) => new object?[] { i + 1, f.From, f.To, Fid(f.SourceRelPath) }));

        // doc chunks
        var docs = docChunks.OrderBy(d => d.Id, o).DistinctBy(d => d.Id).ToList();
        Insert(cn, tx, "INSERT INTO doc_chunk VALUES($a,$b,$c,$d,$e,$f,$g)",
            docs.Select(d => new object?[] { d.Id, Fid(d.SourceRelPath), d.Page, d.Text, d.PageOrSheet ?? d.Page.ToString(CultureInfo.InvariantCulture), d.Kind, d.Sha256 }));
        foreach (var d in docs)
        {
            fts.Add(("doc_chunk", d.Id, $"{d.SourceRelPath} p.{d.PageOrSheet ?? d.Page.ToString(CultureInfo.InvariantCulture)}", d.Text));
        }

        // graph
        var nodeList = nodes.ToList();
        var nodeId = new Dictionary<(string, string), long>();
        for (var i = 0; i < nodeList.Count; i++)
        {
            nodeId[nodeList[i].Key] = i + 1;
        }

        Insert(cn, tx, "INSERT INTO node VALUES($a,$b,$c,$d,$e)", nodeList.Select((n, i) => new object?[] { i + 1, n.Key.Kind, n.Key.Key, n.Value.Label, n.Value.Props }));
        var edgeRows = edges
            .Where(e => nodeId.ContainsKey((e.SK, e.SKey)) && nodeId.ContainsKey((e.DK, e.DKey)))
            .Select(e => (Src: nodeId[(e.SK, e.SKey)], e.Rel, Dst: nodeId[(e.DK, e.DKey)], e.Ev, e.Note))
            .Distinct()
            .OrderBy(e => e.Src).ThenBy(e => e.Rel, o).ThenBy(e => e.Dst).ThenBy(e => e.Ev, o).ThenBy(e => e.Note, o)
            .ToList();
        Insert(cn, tx, "INSERT INTO edge VALUES($a,$b,$c,$d,$e)", edgeRows.Select(e => new object?[] { e.Src, e.Rel, e.Dst, e.Ev, e.Note }));

        Insert(cn, tx, "INSERT INTO search_fts(rowid, kind, key, label, body) VALUES($a,$b,$c,$d,$e)",
            fts.OrderBy(f => f.Kind, o).ThenBy(f => f.Key, o).Select((f, i) => new object?[] { i + 1, f.Kind, f.Key, f.Label, f.Body }));
    }

    private static string FileStem(string path)
    {
        var s = path.Replace('\\', '/');
        s = s[(s.LastIndexOf('/') + 1)..];
        var dot = s.LastIndexOf('.');
        return dot > 0 ? s[..dot] : s;
    }

    private static string Clip(string s) => s.Length <= 120 ? s : s[..120] + "...";

    private static void AddExtra(Dictionary<string, List<string>> map, string block, string text)
    {
        if (!map.TryGetValue(block, out var l))
        {
            map[block] = l = [];
        }

        l.Add(text);
    }

    /// <summary>Block names that appear in the macro as a whole token (not embedded in a longer identifier); names shorter than 3 chars are ignored.</summary>
    private static IEnumerable<string> BlockRefs(string? macro, List<BlockRecord> blockList)
    {
        if (string.IsNullOrEmpty(macro))
        {
            yield break;
        }

        foreach (var b in blockList)
        {
            if (b.Name.Length < 3)
            {
                continue;
            }

            for (var i = macro.IndexOf(b.Name, StringComparison.OrdinalIgnoreCase); i >= 0; i = macro.IndexOf(b.Name, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                var before = i == 0 ? ' ' : macro[i - 1];
                var j = i + b.Name.Length;
                var after = j >= macro.Length ? ' ' : macro[j];
                if (!IsIdentChar(before) && !IsIdentChar(after))
                {
                    yield return b.Name;
                    break;
                }
            }
        }
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '$';

    private static string Json(object v) => JsonSerializer.Serialize(v, CompactJson);

    private static readonly JsonSerializerOptions CompactJson = new(Manifest.JsonOptions) { WriteIndented = false };

    private static void Exec(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static readonly string[] ParamNames = Enumerable.Range(0, 26).Select(i => "$" + (char)('a' + i)).ToArray();

    private static void Insert(SqliteConnection cn, SqliteTransaction tx, string sql, IEnumerable<object?[]> rows)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        SqliteParameter[]? ps = null;
        foreach (var row in rows)
        {
            if (ps is null)
            {
                ps = row.Select((_, i) => cmd.Parameters.Add(new SqliteParameter(ParamNames[i], null))).ToArray();
                cmd.Prepare();
            }

            for (var i = 0; i < row.Length; i++)
            {
                ps[i].Value = row[i] ?? DBNull.Value;
            }

            cmd.ExecuteNonQuery();
        }
    }
}
