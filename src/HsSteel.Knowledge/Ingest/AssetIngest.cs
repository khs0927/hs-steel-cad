using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HsSteel.Assets.Dialogs;
using HsSteel.Assets.Slides;
using HsSteel.Assets.Support;
using HsSteel.Assets.Templates;
using Microsoft.Data.Sqlite;

namespace HsSteel.Knowledge.Ingest;

/// <summary>Result of <see cref="AssetIngest.Apply"/>.</summary>
public sealed record IngestSummary(
    IReadOnlyDictionary<string, int> Counts, IReadOnlyList<string> Failures, int IngestRows, int Covered, int Deferred, int Uncovered, string CoveragePath)
{
    /// <summary>One-line console summary: row counts per new table and ingest coverage.</summary>
    public string Summary =>
        "assets-ingest: " + string.Join(" ", Counts.Select(c => c.Key + "=" + c.Value)) +
        $" | coverage {Covered}/{IngestRows} covered, {Deferred} deferred, {Uncovered} uncovered, failures={Failures.Count} -> {CoveragePath}";
}

/// <summary>
/// Phase-2 asset ingest (slides, icons, DCL dialogs, new-project templates, workbook structure) appended to an already built hs_assets.db,
/// plus the manifest coverage ledger. Everything is sorted ordinally and written in one transaction, so a rebuild is byte-identical.
/// </summary>
public static class AssetIngest
{
    public const string Schema = """
        CREATE TABLE slide(id INTEGER PRIMARY KEY, name TEXT NOT NULL, source_file_id INTEGER REFERENCES source_file(id), width INTEGER NOT NULL, height INTEGER NOT NULL,
            level INTEGER NOT NULL, vector_count INTEGER NOT NULL, solid_count INTEGER NOT NULL, color_changes INTEGER NOT NULL, svg TEXT NOT NULL);
        CREATE INDEX ix_slide_name ON slide(name);
        CREATE TABLE icon(id INTEGER PRIMARY KEY, name TEXT NOT NULL, source_file_id INTEGER REFERENCES source_file(id), width INTEGER NOT NULL, height INTEGER NOT NULL,
            bits INTEGER, mime TEXT NOT NULL, data BLOB NOT NULL, orig_format TEXT NOT NULL, orig_size INTEGER NOT NULL, orig_sha256 TEXT NOT NULL);
        CREATE INDEX ix_icon_name ON icon(name);
        CREATE TABLE dialog(id INTEGER PRIMARY KEY, name TEXT NOT NULL, kind TEXT NOT NULL, base TEXT NOT NULL, label TEXT, source_file_id INTEGER REFERENCES source_file(id), tile_count INTEGER NOT NULL);
        CREATE INDEX ix_dialog_name ON dialog(name);
        CREATE TABLE dialog_field(id INTEGER PRIMARY KEY, dialog_id INTEGER NOT NULL REFERENCES dialog(id), ord INTEGER NOT NULL, depth INTEGER NOT NULL, tile_type TEXT NOT NULL,
            key TEXT, label TEXT, default_value TEXT, list_items TEXT NOT NULL, attrs TEXT NOT NULL, is_reference INTEGER NOT NULL);
        CREATE TABLE project_default(id INTEGER PRIMARY KEY, source_file_id INTEGER NOT NULL REFERENCES source_file(id), ord INTEGER NOT NULL, key TEXT NOT NULL, value TEXT, value_kind TEXT NOT NULL);
        CREATE INDEX ix_project_default_key ON project_default(key);
        CREATE TABLE template_dat_row(id INTEGER PRIMARY KEY, source_file_id INTEGER NOT NULL REFERENCES source_file(id), ord INTEGER NOT NULL, kind TEXT NOT NULL, key TEXT, values_json TEXT NOT NULL);
        CREATE TABLE drafting_style(id INTEGER PRIMARY KEY, source_file_id INTEGER NOT NULL REFERENCES source_file(id), category TEXT NOT NULL, name TEXT NOT NULL, props_json TEXT NOT NULL);
        CREATE INDEX ix_drafting_style_cat ON drafting_style(category, name);
        CREATE TABLE workbook(id INTEGER PRIMARY KEY, source_file_id INTEGER NOT NULL REFERENCES source_file(id), has_vba INTEGER NOT NULL, vba_parts TEXT NOT NULL, defined_names TEXT NOT NULL, sheet_count INTEGER NOT NULL);
        CREATE TABLE workbook_sheet(id INTEGER PRIMARY KEY, workbook_id INTEGER NOT NULL REFERENCES workbook(id), source_file_id INTEGER NOT NULL REFERENCES source_file(id), ord INTEGER NOT NULL,
            name TEXT NOT NULL, state TEXT NOT NULL, dimension TEXT, header_row INTEGER NOT NULL, headers_json TEXT NOT NULL, row_count INTEGER NOT NULL, col_count INTEGER NOT NULL, formula_count INTEGER NOT NULL);
        CREATE TABLE palette_catalog(id INTEGER PRIMARY KEY, source_file_id INTEGER NOT NULL REFERENCES source_file(id), ord INTEGER NOT NULL, palette_id TEXT NOT NULL, name TEXT, href TEXT);
        CREATE TABLE ingest_failure(id INTEGER PRIMARY KEY, source_file_id INTEGER REFERENCES source_file(id), stage TEXT NOT NULL, message TEXT NOT NULL);
        """;

    // children before parents (foreign keys are enforced on drop)
    private static readonly string[] OwnTables =
        ["ingest_failure", "palette_catalog", "workbook_sheet", "workbook", "drafting_style", "template_dat_row", "project_default", "dialog_field", "dialog", "icon", "slide"];

    private static readonly string[] OwnNodeKinds = ["slide", "icon", "dialog"];
    private static readonly string[] OwnEdgeRels = ["illustrates", "has_icon", "dialog_for"];

    private static readonly StringComparer O = StringComparer.Ordinal;
    private static readonly StringComparer Oi = StringComparer.OrdinalIgnoreCase;

    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record Failure(string Rel, string Stage, string Message);

    public static IngestSummary Apply(string dbPath, Manifest manifest, string root, string coveragePath)
    {
        var failures = new List<Failure>();
        var counts = new SortedDictionary<string, int>(O);

        // ---- read & parse everything outside the transaction -------------------------------------------------
        string Abs(string rel) => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        var ingestFiles = manifest.Files.Where(f => f.Disposition == ManifestScanner.Ingest).OrderBy(f => f.RelPath, O).ToList();
        var dclFiles = manifest.Files.Where(f => f.Disposition != ManifestScanner.Excluded && f.RelPath.EndsWith(".dcl", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.RelPath, O).ToList();

        var slides = new List<(string Rel, string Name, SlideDoc Doc, string Svg)>();
        foreach (var f in ingestFiles.Where(f => f.Target == "slides" && f.RelPath.EndsWith(".sld", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var doc = SlideReader.Parse(File.ReadAllBytes(Abs(f.RelPath)));
                slides.Add((f.RelPath, Path.GetFileNameWithoutExtension(f.RelPath), doc, SlideReader.ToSvg(doc)));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failures.Add(new Failure(f.RelPath, "slide", e.Message));
            }
        }

        var icons = new List<(string Rel, string Name, IconImage Img, string Fmt, long Size, string Sha)>();
        foreach (var f in ingestFiles.Where(f => f.Target == "icons"))
        {
            try
            {
                var bytes = File.ReadAllBytes(Abs(f.RelPath));
                var img = IconCodec.Normalize(bytes);

                icons.Add((f.RelPath, Path.GetFileName(f.RelPath), img, Path.GetExtension(f.RelPath).TrimStart('.').ToLowerInvariant(), bytes.Length, f.Sha256));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failures.Add(new Failure(f.RelPath, "icon", e.Message));
            }
        }

        var dialogs = new List<(string Rel, DclDefinition Def)>();
        foreach (var f in dclFiles)
        {
            try
            {
                var file = DclParser.Parse(DclParser.Decode(File.ReadAllBytes(Abs(f.RelPath))));
                dialogs.AddRange(file.Definitions.Select(d => (f.RelPath, d)));
                if (file.Definitions.Count == 0)
                {
                    failures.Add(new Failure(f.RelPath, "dialog", "no dialog definitions found" + (file.Warnings.Count > 0 ? ": " + file.Warnings[0] : string.Empty)));
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failures.Add(new Failure(f.RelPath, "dialog", e.Message));
            }
        }

        var datFiles = new List<(string Rel, string Target, DatFile Dat)>();
        foreach (var f in ingestFiles.Where(f => f.RelPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) && (f.Target == "project-defaults" || f.Target.StartsWith("templates", StringComparison.Ordinal))))
        {
            try
            {
                datFiles.Add((f.RelPath, f.Target, DatFiles.Parse(DatFiles.Decode(File.ReadAllBytes(Abs(f.RelPath))))));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failures.Add(new Failure(f.RelPath, "dat", e.Message));
            }
        }

        var styles = new List<(string Rel, DraftingStyle Style)>();
        foreach (var f in ingestFiles.Where(f => f.RelPath.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) && f.Target.StartsWith("templates", StringComparison.Ordinal)))
        {
            try
            {
                styles.AddRange(DwgStyleReader.Read(Abs(f.RelPath)).Select(s => (f.RelPath, s)));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failures.Add(new Failure(f.RelPath, "drafting_style", e.GetType().Name + ": " + e.Message));
            }
        }

        var books = new List<(string Rel, WorkbookInfo Info)>();
        foreach (var f in ingestFiles.Where(f => f.RelPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.RelPath.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                books.Add((f.RelPath, XlsxStructure.Read(Abs(f.RelPath))));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failures.Add(new Failure(f.RelPath, "workbook", e.GetType().Name + ": " + e.Message));
            }
        }

        var catalogRows = new List<(string Rel, int Ord, string Id, string? Name, string? Href)>();
        foreach (var f in ingestFiles.Where(f => f.RelPath.EndsWith("AcTpCatalog.atc", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var x = System.Xml.Linq.XDocument.Load(Abs(f.RelPath));
                var ord = 0;
                foreach (var p in x.Descendants("Palette").Where(p => p.Element("Url") != null))
                {
                    catalogRows.Add((f.RelPath, ord++, p.Element("ItemID")?.Attribute("idValue")?.Value ?? string.Empty,
                        p.Element("Properties")?.Element("ItemName")?.Value, p.Element("Url")?.Attribute("href")?.Value?.Replace('\\', '/')));
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failures.Add(new Failure(f.RelPath, "palette_catalog", e.Message));
            }
        }

        // command / palette tool images for icon links
        SupportAssets? support = null;
        var supportDir = Path.Combine(root, "HSSTEEL", "support");
        if (Directory.Exists(supportDir))
        {
            try
            {
                support = SupportCatalog.Load(supportDir);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failures.Add(new Failure("HSSTEEL/support", "icon-links", e.Message));
            }
        }

        // ---- write ------------------------------------------------------------------------------------------
        var full = Path.GetFullPath(dbPath);
        using var cn = new SqliteConnection(KnowledgeDbBuilder.ConnectionString(full, SqliteOpenMode.ReadWrite));
        cn.Open();
        using (var tx = cn.BeginTransaction())
        {
            foreach (var t in OwnTables)
            {
                Exec(cn, tx, "DROP TABLE IF EXISTS " + t);
            }

            Exec(cn, tx, "DELETE FROM edge WHERE rel IN (" + string.Join(",", OwnEdgeRels.Select(r => "'" + r + "'")) + ")");
            Exec(cn, tx, "DELETE FROM node WHERE kind IN (" + string.Join(",", OwnNodeKinds.Select(r => "'" + r + "'")) + ")");
            Exec(cn, tx, "DELETE FROM search_fts WHERE kind IN (" + string.Join(",", OwnNodeKinds.Select(r => "'" + r + "'")) + ")");
            Exec(cn, tx, Schema);

            var fileId = new Dictionary<string, long>(O);
            using (var q = Cmd(cn, tx, "SELECT id, rel_path FROM source_file"))
            using (var r = q.ExecuteReader())
            {
                while (r.Read())
                {
                    fileId[r.GetString(1)] = r.GetInt64(0);
                }
            }

            object? Fid(string rel) => fileId.TryGetValue(rel.Split('!')[0].Replace('\\', '/'), out var id) ? id : null;

            var nodeId = new Dictionary<(string, string), long>();
            var nodeIdCi = new Dictionary<(string, string), long>();
            long maxNode = 0;
            using (var q = Cmd(cn, tx, "SELECT id, kind, key FROM node"))
            using (var r = q.ExecuteReader())
            {
                while (r.Read())
                {
                    nodeId[(r.GetString(1), r.GetString(2))] = r.GetInt64(0);
                    nodeIdCi.TryAdd((r.GetString(1), r.GetString(2).ToUpperInvariant()), r.GetInt64(0));
                    maxNode = Math.Max(maxNode, r.GetInt64(0));
                }
            }

            long maxFts = 0;
            using (var q = Cmd(cn, tx, "SELECT COALESCE(MAX(rowid),0) FROM search_fts"))
            {
                maxFts = Convert.ToInt64(q.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            var newNodes = new SortedDictionary<(string Kind, string Key), (string Label, string Props)>(Comparer<(string, string)>.Create((a, b) =>
            {
                var c = O.Compare(a.Item1, b.Item1);
                return c != 0 ? c : O.Compare(a.Item2, b.Item2);
            }));
            var newEdges = new List<(string SK, string SKey, string Rel, string DK, string DKey, string? Ev, string? Note)>();
            var fts = new List<(string Kind, string Key, string Label, string Body)>();

            // slides
            var slideRows = slides.OrderBy(s => s.Name, O).ThenBy(s => s.Rel, O).ToList();
            Insert(cn, tx, "INSERT INTO slide VALUES($a,$b,$c,$d,$e,$f,$g,$h,$i,$j)", slideRows.Select((s, i) => new object?[]
                { i + 1, s.Name, Fid(s.Rel), s.Doc.Width, s.Doc.Height, s.Doc.Level, s.Doc.VectorCount, s.Doc.SolidCount, s.Doc.ColorChanges, s.Svg }));
            var slideKeys = new HashSet<string>(O);
            foreach (var s in slideRows)
            {
                var key = slideKeys.Add(s.Name) ? s.Name : s.Name + "#" + s.Rel;
                newNodes[("slide", key)] = (s.Name, Json(new SortedDictionary<string, object?>
                    { ["width"] = s.Doc.Width, ["height"] = s.Doc.Height, ["vectors"] = s.Doc.VectorCount, ["solids"] = s.Doc.SolidCount, ["source"] = s.Rel }));
                var linked = new List<string>();
                foreach (var kind in new[] { "family", "block", "command", "lisp" })
                {
                    if (nodeIdCi.TryGetValue((kind, s.Name.ToUpperInvariant()), out _))
                    {
                        var target = nodeId.Keys.Where(k => k.Item1 == kind && Oi.Equals(k.Item2, s.Name)).Select(k => k.Item2).OrderBy(x => x, O).First();
                        newEdges.Add(("slide", key, "illustrates", kind, target, s.Rel, $"slide file name equals {kind} name '{target}'"));
                        linked.Add(target);
                    }
                }

                fts.Add(("slide", key, s.Name, "slide " + string.Join(' ', linked.Distinct(O))));
            }

            // dialogs
            var dlgRows = dialogs.OrderBy(d => d.Rel, O).ThenBy(d => d.Def.Name, O).ToList();
            Insert(cn, tx, "INSERT INTO dialog VALUES($a,$b,$c,$d,$e,$f,$g)", dlgRows.Select((d, i) => new object?[]
                { i + 1, d.Def.Name, d.Def.Kind, d.Def.Base, d.Def.Label, Fid(d.Rel), d.Def.Tiles.Count }));
            Insert(cn, tx, "INSERT INTO dialog_field VALUES($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k)", EnumerateFields(dlgRows));
            var dlgKeys = new HashSet<string>(O);
            var commandNames = nodeId.Keys.Where(k => k.Item1 is "command" or "lisp" or "command_alias").ToList();
            foreach (var d in dlgRows)
            {
                var key = dlgKeys.Add(d.Def.Name) ? d.Def.Name : d.Def.Name + "#" + Path.GetFileNameWithoutExtension(d.Rel);
                newNodes[("dialog", key)] = (d.Def.Name, Json(new SortedDictionary<string, object?>
                    { ["kind"] = d.Def.Kind, ["base"] = d.Def.Base, ["label"] = d.Def.Label, ["source"] = d.Rel }));
                var linked = new List<string>();
                if (d.Def.Kind == "dialog")
                {
                    foreach (var k in commandNames.Where(k => Oi.Equals(k.Item2, d.Def.Name)).OrderBy(k => k.Item1, O).ThenBy(k => k.Item2, O))
                    {
                        newEdges.Add(("dialog", key, "dialog_for", k.Item1, k.Item2, d.Rel, $"dialog name equals {k.Item1} name '{k.Item2}'"));
                        linked.Add(k.Item2);
                    }
                }

                fts.Add(("dialog", key, d.Def.Name, string.Join(' ', new[] { d.Def.Label ?? string.Empty }
                    .Concat(d.Def.Tiles.SelectMany(t => new[] { t.Key ?? string.Empty, t.Label ?? string.Empty })).Where(x => x.Length > 0).Concat(linked))));
            }

            // icons
            var iconRows = icons.OrderBy(i => i.Rel, O).ToList();
            Insert(cn, tx, "INSERT INTO icon VALUES($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k)", iconRows.Select((i, n) => new object?[]
                { n + 1, i.Name, Fid(i.Rel), i.Img.Width, i.Img.Height, i.Img.BitsPerPixel, i.Img.Mime, i.Img.Data, i.Fmt, i.Size, i.Sha }));
            var iconByRel = iconRows.ToDictionary(i => i.Rel, i => i, Oi);
            var iconsByName = new Dictionary<string, List<string>>(Oi);
            foreach (var i in iconRows)
            {
                if (!iconsByName.TryGetValue(Uri.UnescapeDataString(i.Name), out var l))
                {
                    iconsByName[Uri.UnescapeDataString(i.Name)] = l = [];
                }

                l.Add(i.Rel);
            }

            var iconLinks = new Dictionary<string, SortedSet<string>>(O); // icon rel -> names of linked commands/palette items
            void LinkIcon(string srcKind, string srcKey, string srcLabel, IEnumerable<string> iconRels, string ev, string note)
            {
                foreach (var rel in iconRels.Distinct(O).OrderBy(x => x, O))
                {
                    newEdges.Add((srcKind, srcKey, "has_icon", "icon", rel, ev, note));
                    if (!iconLinks.TryGetValue(rel, out var set))
                    {
                        iconLinks[rel] = set = new SortedSet<string>(O);
                    }

                    set.Add(srcLabel);
                }
            }

            IEnumerable<string> ByName(string image)
            {
                var n = Uri.UnescapeDataString(image.Replace('\\', '/').Split('/')[^1]);
                if (!iconsByName.TryGetValue(n, out var hits) && Path.GetExtension(n).Length == 0)
                {
                    hits = iconsByName.TryGetValue(n + ".bmp", out var b) ? b : iconsByName.TryGetValue(n + ".png", out var p) ? p : null;
                }

                if (hits is null)
                {
                    return [];
                }

                var preferred = hits.Where(h => h.StartsWith("HSSTEEL/Icons/", StringComparison.OrdinalIgnoreCase) && !h.Contains("/IconBak/", StringComparison.OrdinalIgnoreCase)).ToList();
                if (preferred.Count > 0)
                {
                    return preferred;
                }

                var nonBak = hits.Where(h => !h.Contains("/IconBak/", StringComparison.OrdinalIgnoreCase)).ToList();
                return nonBak.Count > 0 ? nonBak : hits;
            }

            if (support is not null)
            {
                var cmdNames = new HashSet<string>(nodeId.Keys.Where(k => k.Item1 == "command").Select(k => k.Item2), O);
                foreach (var g in support.Commands.Where(c => !string.IsNullOrEmpty(c.Image) && cmdNames.Contains(c.Name)).GroupBy(c => c.Name, O).OrderBy(g => g.Key, O))
                {
                    foreach (var c in g.OrderBy(c => c.Image, O).DistinctBy(c => c.Image))
                    {
                        LinkIcon("command", g.Key, g.Key, ByName(c.Image!), "HSSTEEL/support/" + c.SourcePath.Split('!')[0],
                            "CommandDef.Image = " + c.Image);
                    }
                }

                // palette items: same ordering/keying as KnowledgeDbBuilder
                var pal = support.PaletteItems.Select(p => (
                        p.Palette, p.Name, Kind: p.BlockName is not null ? "block" : p.Command is not null ? "command" : "none",
                        Target: p.BlockName ?? p.Command, Src: "HSSTEEL/support/" + p.SourcePath, Image: p.ImageFile))
                    .OrderBy(p => p.Palette, O).ThenBy(p => p.Name, O).ThenBy(p => p.Kind, O).ThenBy(p => p.Target, O).ThenBy(p => p.Src, O).ToList();
                var seen = new Dictionary<string, int>(O);
                foreach (var p in pal)
                {
                    var baseKey = p.Palette + "/" + p.Name;
                    var n = seen.TryGetValue(baseKey, out var c) ? c + 1 : 1;
                    seen[baseKey] = n;
                    var key = n == 1 ? baseKey : baseKey + "#" + n.ToString(CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(p.Image) || !nodeId.ContainsKey(("palette_item", key)))
                    {
                        continue;
                    }

                    IEnumerable<string> hits = [];
                    if (!p.Src.Contains('!', StringComparison.Ordinal))
                    {
                        var dir = p.Src[..p.Src.LastIndexOf('/')];
                        var rel = (dir + "/" + p.Image.Replace('\\', '/')).Replace("/./", "/");
                        if (iconByRel.TryGetValue(rel, out var hit))
                        {
                            hits = [hit.Rel];
                        }
                    }

                    var list = hits.ToList();
                    if (list.Count == 0)
                    {
                        list = ByName(p.Image).ToList();
                    }

                    LinkIcon("palette_item", key, p.Name, list, p.Src.Split('!')[0], "palette tool image " + p.Image);
                }
            }

            foreach (var i in iconRows)
            {
                newNodes[("icon", i.Rel)] = (i.Name, Json(new SortedDictionary<string, object?> { ["width"] = i.Img.Width, ["height"] = i.Img.Height, ["format"] = i.Fmt }));
                fts.Add(("icon", i.Rel, i.Name, "icon " + Path.GetFileNameWithoutExtension(i.Name) + " " + string.Join(' ', iconLinks.TryGetValue(i.Rel, out var s) ? s : [])));
            }

            // templates
            var pdRows = new List<object?[]>();
            var trRows = new List<object?[]>();
            foreach (var d in datFiles.OrderBy(d => d.Rel, O))
            {
                if (d.Dat.Shape == DatFiles.LineList)
                {
                    trRows.Add([0L, Fid(d.Rel), 0, "linelist", d.Dat.Name, JsonSerializer.Serialize(d.Dat.Lines, Compact)]);
                    continue;
                }

                if (d.Dat.Header is not null)
                {
                    pdRows.Add([0L, Fid(d.Rel), -1, "#header", d.Dat.Header, "header"]);
                }

                foreach (var e in d.Dat.Entries)
                {
                    if (e.Atoms.Count <= 1)
                    {
                        pdRows.Add([0L, Fid(d.Rel), e.Ord, e.Key, e.Value, e.Atoms.Count == 0 ? "flag" : e.ValueKind]);
                    }
                    else
                    {
                        trRows.Add([0L, Fid(d.Rel), e.Ord, "row", e.Key, JsonSerializer.Serialize(e.Atoms, Compact)]);
                    }
                }
            }

            Insert(cn, tx, "INSERT INTO project_default VALUES($a,$b,$c,$d,$e,$f)", pdRows.Select((r, i) => { r[0] = (long)i + 1; return r; }));
            Insert(cn, tx, "INSERT INTO template_dat_row VALUES($a,$b,$c,$d,$e,$f)", trRows.Select((r, i) => { r[0] = (long)i + 1; return r; }));

            var sty = styles.OrderBy(s => s.Rel, O).ThenBy(s => s.Style.Category, O).ThenBy(s => s.Style.Name, O).ToList();
            Insert(cn, tx, "INSERT INTO drafting_style VALUES($a,$b,$c,$d,$e)", sty.Select((s, i) => new object?[]
                { i + 1, Fid(s.Rel), s.Style.Category, s.Style.Name, JsonSerializer.Serialize(s.Style.Props.OrderBy(p => p.Key, O).ToDictionary(p => p.Key, p => p.Value), Compact) }));

            var bk = books.OrderBy(b => b.Rel, O).ToList();
            Insert(cn, tx, "INSERT INTO workbook VALUES($a,$b,$c,$d,$e,$f)", bk.Select((b, i) => new object?[]
                { i + 1, Fid(b.Rel), b.Info.HasVba ? 1 : 0, JsonSerializer.Serialize(b.Info.VbaParts, Compact), JsonSerializer.Serialize(b.Info.DefinedNames, Compact), b.Info.Sheets.Count }));
            long sheetId = 0;
            Insert(cn, tx, "INSERT INTO workbook_sheet VALUES($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k,$l)", bk.SelectMany((b, bi) => b.Info.Sheets.Select(s => new object?[]
                { ++sheetId, bi + 1, Fid(b.Rel), s.Ord, s.Name, s.State, s.Dimension, s.HeaderRow, JsonSerializer.Serialize(s.Headers, Compact), s.RowCount, s.ColCount, s.FormulaCount })));

            Insert(cn, tx, "INSERT INTO palette_catalog VALUES($a,$b,$c,$d,$e,$f)", catalogRows.OrderBy(c => c.Rel, O).ThenBy(c => c.Ord).Select((c, i) => new object?[]
                { i + 1, Fid(c.Rel), c.Ord, c.Id, c.Name, c.Href }));

            var fl = failures.OrderBy(f => f.Rel, O).ThenBy(f => f.Stage, O).ThenBy(f => f.Message, O).ToList();
            Insert(cn, tx, "INSERT INTO ingest_failure VALUES($a,$b,$c,$d)", fl.Select((f, i) => new object?[] { i + 1, Fid(f.Rel), f.Stage, f.Message }));

            // graph + search rows
            var kinds = newNodes.Keys.ToList();
            Insert(cn, tx, "INSERT INTO node VALUES($a,$b,$c,$d,$e)", kinds.Select((k, i) =>
            {
                nodeId[k] = maxNode + i + 1;
                return new object?[] { maxNode + i + 1, k.Kind, k.Key, newNodes[k].Label, newNodes[k].Props };
            }));
            var edgeRows = newEdges
                .Where(e => nodeId.ContainsKey((e.SK, e.SKey)) && nodeId.ContainsKey((e.DK, e.DKey)))
                .Select(e => (Src: nodeId[(e.SK, e.SKey)], e.Rel, Dst: nodeId[(e.DK, e.DKey)], e.Ev, e.Note))
                .Distinct()
                .OrderBy(e => e.Src).ThenBy(e => e.Rel, O).ThenBy(e => e.Dst).ThenBy(e => e.Ev, O).ThenBy(e => e.Note, O)
                .ToList();
            Insert(cn, tx, "INSERT INTO edge VALUES($a,$b,$c,$d,$e)", edgeRows.Select(e => new object?[] { e.Src, e.Rel, e.Dst, e.Ev, e.Note }));
            Insert(cn, tx, "INSERT INTO search_fts(rowid, kind, key, label, body) VALUES($a,$b,$c,$d,$e)",
                fts.OrderBy(f => f.Kind, O).ThenBy(f => f.Key, O).Select((f, i) => new object?[] { maxFts + i + 1, f.Kind, f.Key, f.Label, f.Body }));

            counts["slide"] = slideRows.Count;
            counts["icon"] = iconRows.Count;
            counts["dialog"] = dlgRows.Count;
            counts["dialog_field"] = dlgRows.Sum(d => d.Def.Tiles.Count);
            counts["project_default"] = pdRows.Count;
            counts["template_dat_row"] = trRows.Count;
            counts["drafting_style"] = sty.Count;
            counts["workbook"] = bk.Count;
            counts["workbook_sheet"] = bk.Sum(b => b.Info.Sheets.Count);
            counts["palette_catalog"] = catalogRows.Count;
            counts["ingest_failure"] = fl.Count;
            counts["edge:illustrates"] = edgeRows.Count(e => e.Rel == "illustrates");
            counts["edge:has_icon"] = edgeRows.Count(e => e.Rel == "has_icon");
            counts["edge:dialog_for"] = edgeRows.Count(e => e.Rel == "dialog_for");
            tx.Commit();
        }

        var summary = Coverage.Write(cn, manifest, coveragePath, root, counts, failures.Select(f => f.Rel + " [" + f.Stage + "] " + f.Message).OrderBy(x => x, O).ToList());
        Exec(cn, null, "INSERT INTO search_fts(search_fts) VALUES('optimize');");
        Exec(cn, null, "VACUUM;");
        return summary;
    }

    private static IEnumerable<object?[]> EnumerateFields(List<(string Rel, DclDefinition Def)> dlgRows)
    {
        long id = 0;
        for (var i = 0; i < dlgRows.Count; i++)
        {
            var tiles = dlgRows[i].Def.Tiles;
            for (var n = 0; n < tiles.Count; n++)
            {
                var t = tiles[n];
                yield return
                [
                    ++id, i + 1L, n, t.Depth, t.Type, t.Key, t.Label, t.Default, JsonSerializer.Serialize(t.ListItems, Compact),
                    JsonSerializer.Serialize(t.Attributes.OrderBy(a => a.Key, O).ToDictionary(a => a.Key, a => a.Value), Compact), t.IsReference ? 1 : 0,
                ];
            }
        }
    }

    internal static string Json(object v) => JsonSerializer.Serialize(v, Compact);

    private static void Exec(SqliteConnection cn, SqliteTransaction? tx, string sql)
    {
        using var cmd = Cmd(cn, tx, sql);
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Cmd(SqliteConnection cn, SqliteTransaction? tx, string sql)
    {
        var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static readonly string[] ParamNames = Enumerable.Range(0, 26).Select(i => "$" + (char)('a' + i)).ToArray();

    private static void Insert(SqliteConnection cn, SqliteTransaction tx, string sql, IEnumerable<object?[]> rows)
    {
        using var cmd = Cmd(cn, tx, sql);
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

    internal static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    internal static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);
}
