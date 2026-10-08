using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;

namespace HsSteel.AssetExtract;

public sealed record ExtractOptions(string Root, string OutDir, string Namespace, string Prefix, string SourceRoot,
    bool ModelspaceAsBlock = false, int ModelspaceMax = 5000);

public sealed record ExtractResult(int FilesTotal, int FilesParsed, IReadOnlyList<string> Failed, int DefinitionsSeen, int UniqueBlocks,
    int TablesSeen, int TableCards, int TableCellsNonEmpty, int DynamicBlocks, int WithAttributes, int OriginNormalized,
    IReadOnlyList<JsonObject> Cards);

/// <summary>
/// DWG(ACadSharp 직접 읽기) → powercad.asset.card/v1 카드. extract_blocks.py(LibreDWG+ezdxf)와 같은 배치·필드 이름을 쓰고,
/// LibreDWG가 버리는 ACAD_TABLE 셀 텍스트를 추가로 담는다.
/// </summary>
public static class Extractor
{
    public const double FarOrigin = 10000.0; // mm: 기준점이 도형 bbox에서 이만큼 멀면 bbox 좌하단으로 재기준(huge origin)
    private static readonly string[] SkipAnon = { "*D", "*X", "*E" }; // 치수/해치 익명 블록 (*T 표 그래픽은 유지)
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Pretty = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    private sealed class Def
    {
        public required string Name, Hash, Source, Status; public string Kind = "block_def";
        public required bool Anon, Dynamic;
        public required double[] Base, OrigBase; public double[]? Offset; public double[][]? Bbox;
        public required JsonArray Entities; public required SortedDictionary<string, int> Types;
        public required List<string> Layers, Nested; public required JsonArray AttDefs;
        public int InsertCount;
    }

    private sealed class Group
    {
        public required Def First; public Dictionary<string, int> Names = new(); public List<string> Sources = new();
        public int Occurrences, InsertCount; public string? TableId;
    }

    private sealed class TableGroup
    {
        public required JsonObject First; public List<string> Sources = new(); public int Occurrences;
    }

    public static ExtractResult Run(ExtractOptions o)
    {
        var files = Directory.EnumerateFiles(o.Root, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var failed = new List<string>();
        var groups = new Dictionary<string, Group>();
        var tgroups = new Dictionary<string, TableGroup>();
        int defs = 0, tablesSeen = 0;
        foreach (var path in files)
        {
            string rel = Path.GetRelativePath(o.Root, path).Replace('\\', '/');
            CadDocument doc;
            try { doc = DwgReader.Read(path); }
            catch (Exception ex) { failed.Add($"{rel}: {ex.GetType().Name}: {ex.Message}"); continue; }
            List<(Def d, string blk)> fileDefs; List<(JsonObject t, string hash, string? blk)> tables;
            try { (fileDefs, tables) = ProcessDocument(doc, rel, o.ModelspaceAsBlock, o.ModelspaceMax); }
            catch (Exception ex) { failed.Add($"{rel}: process {ex.GetType().Name}: {ex.Message}"); continue; }
            var tableByBlock = new Dictionary<string, string>();
            foreach (var (t, h, blk) in tables)
            {
                tablesSeen++;
                if (!tgroups.TryGetValue(h, out var tg)) tgroups[h] = tg = new TableGroup { First = t };
                tg.Occurrences++; if (!tg.Sources.Contains(rel)) tg.Sources.Add(rel);
                if (blk is not null) tableByBlock[blk] = h;
            }
            foreach (var (d, _) in fileDefs)
            {
                defs++;
                if (!groups.TryGetValue(d.Hash, out var g)) groups[d.Hash] = g = new Group { First = d };
                g.Names[d.Name] = g.Names.GetValueOrDefault(d.Name) + 1;
                g.Occurrences++; g.InsertCount += d.InsertCount;
                if (!g.Sources.Contains(rel)) g.Sources.Add(rel);
                if (tableByBlock.TryGetValue(d.Name, out var th)) g.TableId ??= th;
            }
        }

        string outDir = o.OutDir;
        Directory.CreateDirectory(Path.Combine(outDir, "blocks"));
        Directory.CreateDirectory(Path.Combine(outDir, "geometry"));
        var cards = new List<JsonObject>();
        string extractor = $"HsSteel.AssetExtract (ACadSharp {typeof(DwgReader).Assembly.GetName().Version}, direct DWG read)";

        // ---- tables first (block cards link to them)
        var tableIds = new Dictionary<string, string>();
        var usedT = new HashSet<string>();
        int cellsNonEmpty = 0;
        foreach (var (h, tg) in tgroups.OrderBy(k => k.Value.Sources[0], StringComparer.Ordinal).ThenBy(k => k.Key, StringComparer.Ordinal))
        {
            string stem = Slug(Path.GetFileNameWithoutExtension(tg.Sources[0])).ToLowerInvariant();
            string id = $"{o.Prefix}table-{stem}-{h[..8]}";
            usedT.Add(id); tableIds[h] = id;
            var t = tg.First;
            cellsNonEmpty += (int)t["non_empty_cells"]!;
            var geom = new JsonObject
            {
                ["schema"] = "powercad.table.content/v1", ["asset_id"] = id, ["rows"] = t["rows"]!.DeepClone(), ["cols"] = t["cols"]!.DeepClone(),
                ["row_heights"] = t["row_heights"]!.DeepClone(), ["col_widths"] = t["col_widths"]!.DeepClone(), ["cells"] = t["cells"]!.DeepClone(),
                ["insert_point"] = t["insert_point"]!.DeepClone(), ["table_block"] = t["table_block"]?.DeepClone(),
            };
            File.WriteAllText(Path.Combine(outDir, "geometry", id + ".json"), geom.ToJsonString(Compact));
            string disp = (string)t["title"]!;
            var card = new JsonObject
            {
                ["schema"] = "powercad.asset.card/v1", ["asset_id"] = id, ["namespace"] = o.Namespace, ["category"] = "table",
                ["block_category"] = "acad_table", ["display_name"] = disp, ["aliases"] = new JsonArray(disp),
                ["format"] = "acad-table", ["license"] = "internal-user-drawings", ["redistributable"] = false, ["units"] = "mm",
                ["geometry_hash"] = h[..16], ["occurrences"] = tg.Occurrences,
                ["rows"] = t["rows"]!.DeepClone(), ["cols"] = t["cols"]!.DeepClone(), ["non_empty_cells"] = t["non_empty_cells"]!.DeepClone(),
                ["width"] = t["width"]!.DeepClone(), ["height"] = t["height"]!.DeepClone(), ["layers"] = new JsonArray(t["layer"]!.DeepClone()),
                ["text_preview"] = t["text_preview"]!.DeepClone(), ["table_block"] = t["table_block"]?.DeepClone(),
                ["source_kind"] = "acad_table", ["geometry_status"] = "extracted", ["geometry_json"] = $"geometry/{id}.json", ["thumb"] = null,
                ["provenance"] = Prov(tg.Sources, (string?)t["table_block"] ?? "ACAD_TABLE", o.SourceRoot, extractor),
            };
            cards.Add(card);
        }

        // ---- block cards (same id/dedup rules as extract_blocks.py)
        string DispName(Group g)
        {
            var named = g.Names.Where(kv => !kv.Key.StartsWith('*')).OrderBy(kv => -kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            return named.Count > 0 ? named[0].Key : g.First.Name;
        }
        var order = groups.OrderBy(kv => DispName(kv.Value).ToUpperInvariant(), StringComparer.Ordinal).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        var used = order.GroupBy(kv => o.Prefix + Slug(DispName(kv.Value)).ToLowerInvariant()).ToDictionary(x => x.Key, x => x.Count());
        var seen = new HashSet<string>(usedT);
        int dyn = 0, withAtt = 0, normCount = 0;
        foreach (var (h, g) in order)
        {
            var f = g.First; string name = DispName(g);
            string aid = o.Prefix + Slug(name).ToLowerInvariant();
            if (used[aid] > 1 || seen.Contains(aid)) aid += "-" + h[..8];
            seen.Add(aid);
            double? w = f.Bbox is null ? null : R(f.Bbox[1][0] - f.Bbox[0][0], 3), hh = f.Bbox is null ? null : R(f.Bbox[1][1] - f.Bbox[0][1], 3);
            var aliases = g.Names.Keys.Append(name.ToUpperInvariant()).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (f.Dynamic) dyn++; if (f.AttDefs.Count > 0) withAtt++; if (f.Offset is not null) normCount++;
            var geom = new JsonObject
            {
                ["schema"] = "powercad.block.geometry/v1", ["asset_id"] = aid, ["block_name"] = f.Name, ["units"] = "mm",
                ["coords"] = "relative to base_point (block base or normalized bbox lower-left)",
                ["base_point"] = Arr(f.Base), ["original_base_point"] = Arr(f.OrigBase), ["origin_offset"] = f.Offset is null ? null : Arr(f.Offset),
                ["attribute_defs"] = f.AttDefs.DeepClone(), ["nested_blocks"] = Strs(f.Nested), ["entities"] = f.Entities.DeepClone(),
            };
            File.WriteAllText(Path.Combine(outDir, "geometry", aid + ".json"), geom.ToJsonString(Compact));
            var card = new JsonObject
            {
                ["schema"] = "powercad.asset.card/v1", ["asset_id"] = aid, ["namespace"] = o.Namespace, ["category"] = "block",
                ["block_category"] = Classify(name), ["display_name"] = name, ["aliases"] = Strs(aliases), ["format"] = "block-def",
                ["license"] = "internal-user-drawings", ["redistributable"] = false, ["units"] = "mm", ["geometry_hash"] = h[..16],
                ["occurrences"] = g.Occurrences, ["insert_count"] = g.InsertCount, ["entity_count"] = f.Entities.Count,
                ["entity_types"] = new JsonObject(f.Types.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
                ["width"] = w, ["height"] = hh,
                ["bbox"] = f.Bbox is null ? null : new JsonArray(Arr(f.Bbox[0]), Arr(f.Bbox[1])),
                ["base_point"] = Arr(f.Base), ["original_base_point"] = Arr(f.OrigBase),
                ["origin_normalized"] = f.Offset is not null, ["origin_offset"] = f.Offset is null ? null : Arr(f.Offset),
                ["layers"] = Strs(f.Layers), ["attribute_defs"] = f.AttDefs.DeepClone(), ["nested_blocks"] = Strs(f.Nested),
                ["is_dynamic"] = f.Dynamic, ["is_anonymous"] = f.Anon, ["source_kind"] = f.Kind, ["geometry_status"] = f.Status,
                ["geometry_json"] = $"geometry/{aid}.json", ["thumb"] = null,
                ["acad_table"] = g.TableId is not null && tableIds.TryGetValue(g.TableId, out var tid) ? tid : null,
                ["provenance"] = Prov(g.Sources, f.Name, o.SourceRoot, extractor, f.Source),
            };
            cards.Add(card);
        }

        foreach (var c in cards)
            File.WriteAllText(Path.Combine(outDir, "blocks", (string)c["asset_id"]! + ".json"), c.ToJsonString(Pretty));
        File.WriteAllLines(Path.Combine(outDir, "catalog.jsonl"), cards.Select(c => c.ToJsonString(Compact)));

        int blockCards = cards.Count(c => (string)c["category"]! == "block");
        var byCat = cards.Where(c => (string)c["category"]! == "block").GroupBy(c => (string)c["block_category"]!).OrderBy(x => x.Key, StringComparer.Ordinal);
        var manifest = new JsonObject
        {
            ["schema"] = "powercad.asset.manifest/v1", ["namespace"] = o.Namespace, ["generated_by"] = extractor,
            ["generated_at"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            ["source_root"] = o.SourceRoot, ["card_count"] = cards.Count, ["block_card_count"] = blockCards, ["unique_blocks"] = blockCards,
            ["definitions_seen"] = defs, ["files_total"] = files.Count, ["files_parsed"] = files.Count - failed.Count, ["files_failed"] = Strs(failed),
            ["thumbs"] = 0, ["modelspace_as_block"] = o.ModelspaceAsBlock,
            ["counts_by_source_kind"] = new JsonObject(cards.Where(c => (string)c["category"]! == "block").GroupBy(c => (string)c["source_kind"]!).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => KeyValuePair.Create(x.Key, (JsonNode?)x.Count()))),
            ["counts_by_block_category"] = new JsonObject(byCat.Select(x => KeyValuePair.Create(x.Key, (JsonNode?)x.Count()))),
            ["named_blocks"] = cards.Count(c => (string)c["category"]! == "block" && !(bool)c["is_anonymous"]!),
            ["dynamic_blocks"] = dyn, ["with_attributes"] = withAtt, ["origin_normalized"] = normCount,
            ["acad_tables_seen"] = tablesSeen, ["table_cards"] = tgroups.Count, ["table_cells_non_empty"] = cellsNonEmpty,
            ["license"] = "internal-user-drawings", ["redistributable"] = false,
            ["layout"] = new JsonObject { ["catalog"] = "catalog.jsonl", ["cards"] = "blocks/<asset_id>.json", ["geometry"] = "geometry/<asset_id>.json" },
        };
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), manifest.ToJsonString(Pretty));
        return new ExtractResult(files.Count, files.Count - failed.Count, failed, defs, blockCards, tablesSeen, tgroups.Count, cellsNonEmpty,
            dyn, withAtt, normCount, cards);
    }

    private static JsonObject Prov(List<string> sources, string blockName, string sourceRoot, string extractor, string? sample = null) => new()
    {
        ["source_drawings"] = Strs(sources.OrderBy(s => s, StringComparer.Ordinal)), ["source_sample"] = sample ?? sources[0],
        ["source_block_name"] = blockName, ["source_root"] = sourceRoot, ["extractor"] = extractor,
    };

    // ------------------------------------------------------------------ per document
    private static (List<(Def, string)>, List<(JsonObject, string, string?)>) ProcessDocument(CadDocument doc, string rel, bool mspAsBlock = false, int mspMax = 5000)
    {
        var insCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var br in doc.BlockRecords)
            foreach (var ins in br.Entities.OfType<Insert>())
                if (ins.Block is not null) insCount[ins.Block.Name] = insCount.GetValueOrDefault(ins.Block.Name) + 1;

        var defs = new List<(Def, string)>();
        var tables = new List<(JsonObject, string, string?)>();
        foreach (var br in doc.BlockRecords)
        {
            foreach (var t in br.Entities.OfType<TableEntity>()) tables.Add(TableOf(t));
            string up = br.Name.ToUpperInvariant();
            if (up.StartsWith("*MODEL_SPACE", StringComparison.Ordinal) || up.StartsWith("*PAPER_SPACE", StringComparison.Ordinal)) continue;
            if (SkipAnon.Any(p => up.StartsWith(p, StringComparison.Ordinal))) continue;
            var ents = br.Entities.ToList();
            bool anon = br.Name.StartsWith('*');
            if (anon && ents.Count < 2) continue;
            var b = br.BlockEntity?.BasePoint ?? new CSMath.XYZ(0, 0, 0);
            defs.Add((Describe(br, ents, new[] { b.X, b.Y, b.Z }, anon, insCount.GetValueOrDefault(br.Name), rel), br.Name));
        }
        if (mspAsBlock)
        {
            // wblock 라이브러리(block/*.dwg)처럼 파일 모형 공간 전체를 파일 이름의 블록 하나로 카드화 (extract_blocks.py --modelspace-as-block 과 동일 규칙)
            var msp = doc.BlockRecords.FirstOrDefault(r => r.Name.Equals("*Model_Space", StringComparison.OrdinalIgnoreCase));
            var ents = msp?.Entities.ToList() ?? new();
            if (ents.Count > 0 && ents.Count <= mspMax)
            {
                var ib = doc.Header.ModelSpaceInsertionBase;
                var d = Describe(msp!, ents, new[] { ib.X, ib.Y, ib.Z }, false, 0, rel, Path.GetFileNameWithoutExtension(rel));
                d.Kind = "file_modelspace";
                defs.Add((d, d.Name));
            }
        }
        return (defs, tables);
    }

    private static Def Describe(BlockRecord br, List<Entity> ents, double[] basePt, bool anon, int insCount, string rel, string? nameOverride = null)
    {
        string status = "extracted";
        var ext = new Ext();
        foreach (var e in ents) foreach (var (x, y) in Pts(e, 0)) ext.Add(x, y);
        var orig = basePt.Select(v => R(v, 2)).ToArray();
        double[]? offset = null;
        if (!ext.Empty)
        {
            double dx = Math.Max(Math.Max(ext.MinX - basePt[0], 0), basePt[0] - ext.MaxX);
            double dy = Math.Max(Math.Max(ext.MinY - basePt[1], 0), basePt[1] - ext.MaxY);
            if (Math.Sqrt(dx * dx + dy * dy) > FarOrigin)
            {
                var nb = new[] { ext.MinX, ext.MinY, basePt[2] };
                offset = Enumerable.Range(0, 3).Select(i => R(nb[i] - basePt[i], 2)).ToArray();
                basePt = nb; status += "_origin_normalized";
            }
        }
        else if (ents.Count > 0) status = "extracted_no_bbox";
        var norm = new JsonArray(); var sigs = new List<string>(); var types = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var layers = new SortedSet<string>(StringComparer.Ordinal); var nested = new SortedSet<string>(StringComparer.Ordinal);
        var attdefs = new JsonArray();
        foreach (var e in ents)
        {
            var d = Norm(e, basePt);
            string type = (string)d["type"]!;
            types[type] = types.GetValueOrDefault(type) + 1;
            layers.Add((string?)d["layer"] ?? "0");
            sigs.Add(Sig(d));
            norm.Add(d);
            if (e is Insert ins and not TableEntity && ins.Block is not null) nested.Add(ins.Block.Name);
            if (e is AttributeDefinition ad)
                attdefs.Add(new JsonObject { ["tag"] = ad.Tag ?? "", ["prompt"] = ad.Prompt ?? "", ["default"] = ad.Value ?? "", ["position"] = V(ad.InsertPoint, basePt) });
        }
        sigs.Sort(StringComparer.Ordinal);
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(string.Join("\n", sigs)))).ToLowerInvariant();
        double[][]? bbox = ext.Empty ? null : new[]
        {
            new[] { R(ext.MinX - basePt[0], 3), R(ext.MinY - basePt[1], 3), 0.0 },
            new[] { R(ext.MaxX - basePt[0], 3), R(ext.MaxY - basePt[1], 3), 0.0 },
        };
        return new Def
        {
            Name = nameOverride ?? br.Name, Hash = hash, Source = rel, Status = status, Anon = anon, Dynamic = nameOverride is null && br.IsDynamic,
            Base = basePt.Select(v => R(v, 2)).ToArray(), OrigBase = orig, Offset = offset, Bbox = bbox, Entities = norm, Types = types,
            Layers = layers.ToList(), Nested = nested.ToList(), AttDefs = attdefs, InsertCount = insCount,
        };
    }

    // ------------------------------------------------------------------ ACAD_TABLE
    public static (JsonObject Json, string Hash, string? Block) TableOf(TableEntity t)
    {
        var cells = new JsonArray(); int nonEmpty = 0; var preview = new List<string>();
        var sb = new StringBuilder();
        foreach (var row in t.Rows)
        {
            var r = new JsonArray();
            foreach (var cell in row.Cells)
            {
                string txt = CellText(cell);
                if (txt.Length > 0) { nonEmpty++; if (preview.Count < 12) preview.Add(txt); }
                r.Add(txt); sb.Append(txt).Append('\u001f');
            }
            cells.Add(r); sb.Append('\u001e');
        }
        int cols = t.Columns.Count;
        sb.Append(t.Rows.Count).Append('x').Append(cols);
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        string title = preview.Count > 0 ? Trunc(preview[0], 60) : $"ACAD_TABLE {t.Rows.Count}x{cols}";
        var o = new JsonObject
        {
            ["rows"] = t.Rows.Count, ["cols"] = cols,
            ["row_heights"] = new JsonArray(t.Rows.Select(x => (JsonNode?)R(x.Height, 3)).ToArray()),
            ["col_widths"] = new JsonArray(t.Columns.Select(x => (JsonNode?)R(x.Width, 3)).ToArray()),
            ["width"] = R(t.Columns.Sum(x => x.Width), 3), ["height"] = R(t.Rows.Sum(x => x.Height), 3),
            ["cells"] = cells, ["non_empty_cells"] = nonEmpty, ["text_preview"] = Strs(preview), ["title"] = title,
            ["layer"] = t.Layer?.Name ?? "0", ["insert_point"] = Arr(new[] { R(t.InsertPoint.X, 3), R(t.InsertPoint.Y, 3), R(t.InsertPoint.Z, 3) }),
            ["table_block"] = t.Block?.Name,
        };
        return (o, hash, t.Block?.Name);
    }

    public static string CellText(TableEntity.Cell cell)
    {
        var parts = new List<string>();
        foreach (var c in cell.Contents ?? new())
        {
            var v = c?.CadValue;
            if (v is null) continue;
            string s = v.FormattedValue ?? v.Value?.ToString() ?? "";
            s = PlainMText(s);
            if (s.Length > 0) parts.Add(s);
        }
        return string.Join("\n", parts);
    }

    // ------------------------------------------------------------------ entity normalization
    private static JsonObject Norm(Entity e, double[] b)
    {
        var d = new JsonObject { ["type"] = e.ObjectName, ["layer"] = e.Layer?.Name ?? "0" };
        switch (e)
        {
            case Line l: d["start"] = V(l.StartPoint, b); d["end"] = V(l.EndPoint, b); break;
            case Arc a: d["center"] = V(a.Center, b); d["radius"] = R(a.Radius, 3); d["start_angle"] = R(Deg(a.StartAngle), 3); d["end_angle"] = R(Deg(a.EndAngle), 3); break;
            case Circle c: d["center"] = V(c.Center, b); d["radius"] = R(c.Radius, 3); break;
            case Ellipse el: d["center"] = V(el.Center, b); d["major_axis"] = Arr(new[] { R(el.MajorAxisEndPoint.X, 3), R(el.MajorAxisEndPoint.Y, 3), R(el.MajorAxisEndPoint.Z, 3) }); d["ratio"] = R(el.RadiusRatio, 4); break;
            case LwPolyline lw:
                d["closed"] = lw.IsClosed;
                d["points"] = new JsonArray(lw.Vertices.Select(v => (JsonNode?)new JsonArray(R(v.Location.X - b[0], 3), R(v.Location.Y - b[1], 3), R(v.Bulge, 4))).ToArray());
                break;
            case Polyline2D p2: d["closed"] = p2.IsClosed; d["points"] = new JsonArray(p2.Vertices.Select(v => (JsonNode?)V(v.Location, b)).ToArray()); break;
            case Polyline3D p3: d["closed"] = p3.IsClosed; d["points"] = new JsonArray(p3.Vertices.Select(v => (JsonNode?)V(v.Location, b)).ToArray()); break;
            case AttributeDefinition ad: d["tag"] = ad.Tag; d["prompt"] = ad.Prompt; d["text"] = ad.Value; d["insert"] = V(ad.InsertPoint, b); d["height"] = R(ad.Height, 3); d["rotation"] = R(Deg(ad.Rotation), 3); break;
            case TextEntity t: d["text"] = t.Value; d["insert"] = V(t.InsertPoint, b); d["height"] = R(t.Height, 3); d["rotation"] = R(Deg(t.Rotation), 3); d["style"] = t.Style?.Name; break;
            case MText m: d["text"] = m.Value; d["plain_text"] = PlainMText(m.Value ?? ""); d["insert"] = V(m.InsertPoint, b); d["height"] = R(m.Height, 3); d["width"] = R(m.RectangleWidth, 3); d["style"] = m.Style?.Name; break;
            case TableEntity tb:
                var (tj, _, _) = TableOf(tb);
                d["insert"] = V(tb.InsertPoint, b); d["rows"] = tj["rows"]!.DeepClone(); d["cols"] = tj["cols"]!.DeepClone(); d["cells"] = tj["cells"]!.DeepClone();
                d["table_block"] = tb.Block?.Name;
                break;
            case Insert ins:
                d["name"] = ins.Block?.Name; d["insert"] = V(ins.InsertPoint, b);
                d["scale"] = Arr(new[] { R(ins.XScale, 4), R(ins.YScale, 4), R(ins.ZScale, 4) }); d["rotation"] = R(Deg(ins.Rotation), 3);
                if (ins.Attributes.Count > 0)
                    d["attribs"] = new JsonArray(ins.Attributes.Select(at => (JsonNode?)new JsonObject { ["tag"] = at.Tag, ["text"] = at.Value }).ToArray());
                break;
            case Point p: d["location"] = V(p.Location, b); break;
            case Solid so: d["points"] = new JsonArray(V(so.FirstCorner, b), V(so.SecondCorner, b), V(so.ThirdCorner, b), V(so.FourthCorner, b)); break;
            case Dimension dim: d["definition_point"] = V(dim.DefinitionPoint, b); d["text"] = dim.Text; d["measurement"] = R(dim.Measurement, 3); break;
            case Hatch h: d["pattern"] = h.Pattern?.Name; d["solid"] = h.IsSolid; d["paths"] = h.Paths.Count; break;
        }
        return d;
    }

    private static string Sig(JsonObject d)
    {
        var skip = new HashSet<string> { "type", "layer", "style" };
        var parts = new List<string> { (string)d["type"]! };
        foreach (var kv in d.Where(kv => !skip.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            parts.Add(kv.Key + "=" + (kv.Value is null ? "null" : RoundForHash(kv.Value).ToJsonString(Compact)));
        return string.Join("|", parts);
    }

    private static JsonNode RoundForHash(JsonNode n) => n switch
    {
        JsonArray a => new JsonArray(a.Select(x => x is null ? null : RoundForHash(x)).ToArray()),
        JsonObject o => new JsonObject(o.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => KeyValuePair.Create(kv.Key, kv.Value is null ? null : RoundForHash(kv.Value)))),
        JsonValue v when v.TryGetValue<double>(out var dbl) => JsonValue.Create(Math.Round(dbl, 2) + 0.0),
        _ => n.DeepClone(),
    };

    // ------------------------------------------------------------------ extents (block-local XY)
    private static IEnumerable<(double, double)> Pts(Entity e, int depth)
    {
        switch (e)
        {
            case Line l: yield return (l.StartPoint.X, l.StartPoint.Y); yield return (l.EndPoint.X, l.EndPoint.Y); break;
            case Arc a:
                {
                    double s = a.StartAngle, sw = a.EndAngle - a.StartAngle; while (sw <= 0) sw += 2 * Math.PI;
                    for (int i = 0; i <= 16; i++) { double t = s + sw * i / 16; yield return (a.Center.X + a.Radius * Math.Cos(t), a.Center.Y + a.Radius * Math.Sin(t)); }
                    break;
                }
            case Circle c: yield return (c.Center.X - c.Radius, c.Center.Y - c.Radius); yield return (c.Center.X + c.Radius, c.Center.Y + c.Radius); break;
            case Ellipse el: foreach (var v in el.PolygonalVertexes(24)) yield return (v.X, v.Y); break;
            case LwPolyline lw: foreach (var v in lw.Vertices) yield return (v.Location.X, v.Location.Y); break;
            case Polyline2D p2: foreach (var v in p2.Vertices) yield return (v.Location.X, v.Location.Y); break;
            case Polyline3D p3: foreach (var v in p3.Vertices) yield return (v.Location.X, v.Location.Y); break;
            case AttributeDefinition ad: yield return (ad.InsertPoint.X, ad.InsertPoint.Y); break;
            case TextEntity t: yield return (t.InsertPoint.X, t.InsertPoint.Y); break;
            case MText m: yield return (m.InsertPoint.X, m.InsertPoint.Y); break;
            case Point p: yield return (p.Location.X, p.Location.Y); break;
            case Solid so: yield return (so.FirstCorner.X, so.FirstCorner.Y); yield return (so.SecondCorner.X, so.SecondCorner.Y); yield return (so.ThirdCorner.X, so.ThirdCorner.Y); yield return (so.FourthCorner.X, so.FourthCorner.Y); break;
            case Insert ins:
                {
                    bool any = false;
                    if (depth < 4 && ins.Block is not null)
                    {
                        var bp = ins.Block.BlockEntity?.BasePoint ?? new CSMath.XYZ(0, 0, 0);
                        double c = Math.Cos(ins.Rotation), s = Math.Sin(ins.Rotation);
                        foreach (var inner in ins.Block.Entities)
                            foreach (var (x0, y0) in Pts(inner, depth + 1))
                            {
                                double x = (x0 - bp.X) * ins.XScale, y = (y0 - bp.Y) * ins.YScale;
                                any = true; yield return (x * c - y * s + ins.InsertPoint.X, x * s + y * c + ins.InsertPoint.Y);
                            }
                    }
                    if (!any) yield return (ins.InsertPoint.X, ins.InsertPoint.Y);
                    break;
                }
        }
    }

    private sealed class Ext
    {
        public double MinX = double.MaxValue, MinY = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue;
        public bool Empty => MinX > MaxX;
        public void Add(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1e12 || Math.Abs(y) > 1e12) return;
            MinX = Math.Min(MinX, x); MinY = Math.Min(MinY, y); MaxX = Math.Max(MaxX, x); MaxY = Math.Max(MaxY, y);
        }
    }

    // ------------------------------------------------------------------ helpers
    public static string Classify(string name)
    {
        string n = name.ToUpperInvariant();
        if (n.StartsWith("*T", StringComparison.Ordinal)) return "table_graphic";
        if (n.StartsWith('*')) return "anonymous";
        foreach (var (kw, cat) in new[] { ("FRAME", "title_block"), ("TITLE", "title_block"), ("TABLE", "table"), ("ANCHOR", "anchor"),
                     ("BOLT", "bolt"), ("BLT", "bolt"), ("WELD", "weld"), ("GRID", "grid"), ("ARROW", "symbol"), ("SECT", "section_mark"),
                     ("LEVEL", "level_mark"), ("EL", "level_mark"), ("MARK", "symbol"), ("PLATE", "plate"), ("PL", "plate") })
            if (n.Contains(kw, StringComparison.Ordinal)) return cat;
        return "misc";
    }

    public static string Slug(string s)
    {
        s = Regex.Replace(s, "[^0-9A-Za-z가-힣_.-]+", "_").Trim('_', '.');
        return s.Length == 0 ? "block" : s[..Math.Min(80, s.Length)];
    }

    public static string PlainMText(string s)
    {
        s = Regex.Replace(s, @"\\[Pp]", "\n");
        s = Regex.Replace(s, @"\\[A-Za-z][^;\\{}]*;", "");
        s = s.Replace("{", "").Replace("}", "").Replace("\\~", " ");
        return s.Trim();
    }

    private static string Trunc(string s, int n) { s = s.Replace('\n', ' '); return s.Length <= n ? s : s[..n]; }
    private static double Deg(double rad) => rad * 180.0 / Math.PI;
    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : 0.0;
    private static JsonArray V(CSMath.XYZ p, double[] b) => new(R(p.X - b[0], 3), R(p.Y - b[1], 3), R(p.Z - b[2], 3));
    private static JsonArray V(CSMath.XY p, double[] b) => new(R(p.X - b[0], 3), R(p.Y - b[1], 3), 0.0);
    private static JsonArray Arr(double[] a) => new(a.Select(x => (JsonNode?)x).ToArray());
    private static JsonArray Strs(IEnumerable<string> a) => new(a.Select(x => (JsonNode?)x).ToArray());
}
