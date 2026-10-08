using System.Text.Json.Nodes;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using HsSteel.AssetExtract;

namespace HsSteel.AssetExtract.Tests;

/// <summary>모든 DWG는 테스트 안에서 ACadSharp DwgWriter로 만든다(사내 자산 비커밋).</summary>
public sealed class AssetExtractTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hs-assetextract-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_dir, "dwg");
    private string Out => Path.Combine(_dir, "out");

    public AssetExtractTests() => Directory.CreateDirectory(Root);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static CadDocument Doc(bool withFar = true)
    {
        var doc = new CadDocument();
        var inner = new BlockRecord("INNER");
        inner.Entities.Add(new Circle { Center = new XYZ(0, 0, 0), Radius = 5 });
        doc.BlockRecords.Add(inner);

        var outer = new BlockRecord("PLATE-OUTER");
        outer.Entities.Add(new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(100, 0, 0) });
        outer.Entities.Add(new Line { StartPoint = new XYZ(100, 0, 0), EndPoint = new XYZ(100, 50, 0) });
        outer.Entities.Add(new Insert(inner) { InsertPoint = new XYZ(50, 25, 0) });
        outer.Entities.Add(new AttributeDefinition { Tag = "MARK", Prompt = "부재 마크", Value = "C001", InsertPoint = new XYZ(10, 10, 0), Height = 3.4 });
        doc.BlockRecords.Add(outer);

        if (withFar)
        {
            // huge origin: 기준점 (0,0), 도형은 (222990,-156448) 근처
            var far = new BlockRecord("FW-FAR");
            far.Entities.Add(new Line { StartPoint = new XYZ(222990, -156448, 0), EndPoint = new XYZ(223090, -156448, 0) });
            far.Entities.Add(new Line { StartPoint = new XYZ(223090, -156448, 0), EndPoint = new XYZ(223090, -156398, 0) });
            doc.BlockRecords.Add(far);
            doc.Entities.Add(new Insert(far) { InsertPoint = new XYZ(0, 0, 0) });
        }
        doc.Entities.Add(new Insert(outer) { InsertPoint = new XYZ(1000, 0, 0) });
        doc.Entities.Add(new Insert(outer) { InsertPoint = new XYZ(2000, 0, 0) });
        return doc;
    }

    private void Write(string rel, CadDocument doc)
    {
        var p = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        DwgWriter.Write(p, doc);
    }

    private static JsonObject Card(ExtractResult r, string display) =>
        r.Cards.Single(c => (string)c["category"]! == "block" && (string)c["display_name"]! == display);

    [Fact]
    public void Writes_drive_db_layout_with_card_v1_fields()
    {
        Write("a.dwg", Doc());
        var r = Extractor.Run(new ExtractOptions(Root, Out, "test-ns", "tx-", "C:\\cad\\TEST"));
        Assert.Equal(1, r.FilesParsed);
        Assert.Empty(r.Failed);
        Assert.True(File.Exists(Path.Combine(Out, "manifest.json")));
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Out, "manifest.json")))!;
        Assert.Equal("powercad.asset.manifest/v1", (string)manifest["schema"]!);
        Assert.Equal("test-ns", (string)manifest["namespace"]!);
        var lines = File.ReadAllLines(Path.Combine(Out, "catalog.jsonl"));
        Assert.Equal(r.Cards.Count, lines.Length);
        foreach (var l in lines)
        {
            var c = JsonNode.Parse(l)!.AsObject();
            Assert.Equal("powercad.asset.card/v1", (string)c["schema"]!);
            string id = (string)c["asset_id"]!;
            Assert.StartsWith("tx-", id);
            Assert.True(File.Exists(Path.Combine(Out, "blocks", id + ".json")));
            Assert.True(File.Exists(Path.Combine(Out, (string)c["geometry_json"]!)));
            foreach (var k in new[] { "namespace", "category", "display_name", "aliases", "geometry_hash", "base_point", "original_base_point",
                         "origin_normalized", "layers", "attribute_defs", "nested_blocks", "provenance" })
                Assert.True(c.ContainsKey(k), $"{id} missing {k}");
        }
    }

    [Fact]
    public void Reads_attribute_defs_nested_blocks_and_insert_counts()
    {
        Write("a.dwg", Doc());
        var r = Extractor.Run(new ExtractOptions(Root, Out, "t", "tx-", Root));
        var outer = Card(r, "PLATE-OUTER");
        var att = outer["attribute_defs"]!.AsArray().Single()!;
        Assert.Equal("MARK", (string)att["tag"]!);
        Assert.Equal("부재 마크", (string)att["prompt"]!);
        Assert.Equal("C001", (string)att["default"]!);
        Assert.Equal(new[] { "INNER" }, outer["nested_blocks"]!.AsArray().Select(x => (string)x!).ToArray());
        Assert.Equal(2, (int)outer["insert_count"]!);
        Assert.Equal(1, (int)Card(r, "INNER")["insert_count"]!);
        Assert.Equal(100.0, (double)outer["width"]!, 3);
        Assert.Equal(50.0, (double)outer["height"]!, 3);
    }

    [Fact]
    public void Normalizes_huge_origin_to_bbox_lower_left()
    {
        Write("a.dwg", Doc());
        var r = Extractor.Run(new ExtractOptions(Root, Out, "t", "tx-", Root));
        var far = Card(r, "FW-FAR");
        Assert.True((bool)far["origin_normalized"]!);
        var off = far["origin_offset"]!.AsArray().Select(x => (double)x!).ToArray();
        Assert.Equal(222990, off[0], 2);
        Assert.Equal(-156448, off[1], 2);
        var bbox = far["bbox"]!.AsArray();
        Assert.Equal(0.0, (double)bbox[0]![0]!, 3);
        Assert.Equal(100.0, (double)bbox[1]![0]!, 3);
        var geom = JsonNode.Parse(File.ReadAllText(Path.Combine(Out, (string)far["geometry_json"]!)))!;
        var first = geom["entities"]!.AsArray()[0]!;
        Assert.Equal(0.0, (double)first["start"]![0]!, 3);
        Assert.False((bool)Card(r, "PLATE-OUTER")["origin_normalized"]!);
    }

    [Fact]
    public void Dedups_identical_definitions_across_files()
    {
        Write("a.dwg", Doc());
        Write("sub/b.dwg", Doc(withFar: false));
        var r = Extractor.Run(new ExtractOptions(Root, Out, "t", "tx-", Root));
        Assert.Equal(2, r.FilesParsed);
        Assert.Equal(5, r.DefinitionsSeen);   // a: INNER, PLATE-OUTER, FW-FAR / b: INNER, PLATE-OUTER
        Assert.Equal(3, r.UniqueBlocks);
        var outer = Card(r, "PLATE-OUTER");
        Assert.Equal(2, (int)outer["occurrences"]!);
        Assert.Equal(new[] { "a.dwg", "sub/b.dwg" }, outer["provenance"]!["source_drawings"]!.AsArray().Select(x => (string)x!).ToArray());
    }

    [Fact]
    public void Modelspace_as_block_cards_each_file()
    {
        Write("lib/WBLOCK-1.dwg", Doc(withFar: false));
        var r = Extractor.Run(new ExtractOptions(Root, Out, "t", "tx-", Root, ModelspaceAsBlock: true));
        var msp = r.Cards.Single(c => (string?)c["source_kind"] == "file_modelspace");
        Assert.Equal("WBLOCK-1", (string)msp["display_name"]!);
        Assert.Contains("PLATE-OUTER", msp["nested_blocks"]!.AsArray().Select(x => (string)x!));
    }

    [Fact]
    public void Corrupt_file_is_reported_not_fatal()
    {
        Write("ok.dwg", Doc());
        File.WriteAllText(Path.Combine(Root, "bad.dwg"), "not a dwg");
        var r = Extractor.Run(new ExtractOptions(Root, Out, "t", "tx-", Root));
        Assert.Equal(2, r.FilesTotal);
        Assert.Equal(1, r.FilesParsed);
        Assert.Single(r.Failed);
        Assert.StartsWith("bad.dwg", r.Failed[0]);
    }

    [Fact]
    public void Table_cells_text_is_extracted()
    {
        var t = new TableEntity();
        t.Columns.Add(new TableEntity.Column { Width = 30 });
        t.Columns.Add(new TableEntity.Column { Width = 40 });
        string[,] txt = { { "MARK", "규격" }, { "C001", "H-400x200x8x13" } };
        for (int i = 0; i < 2; i++)
        {
            var row = new TableEntity.Row { Height = 8 };
            for (int j = 0; j < 2; j++)
            {
                var cell = new TableEntity.Cell();
                var content = new TableEntity.CellContent();
                content.CadValue.SetValue(txt[i, j], CadValueType.String);
                cell.Contents.Add(content);
                row.Cells.Add(cell);
            }
            t.Rows.Add(row);
        }
        var (json, hash, _) = Extractor.TableOf(t);
        Assert.Equal(2, (int)json["rows"]!);
        Assert.Equal(2, (int)json["cols"]!);
        Assert.Equal(4, (int)json["non_empty_cells"]!);
        Assert.Equal("H-400x200x8x13", (string)json["cells"]![1]![1]!);
        Assert.Equal("MARK", (string)json["title"]!);
        Assert.Equal(70.0, (double)json["width"]!, 3);
        Assert.Equal(40, hash.Length);
    }

    [Theory]
    [InlineData(@"{\fArial|b0;\C1;MARK}\PNO", "MARK\nNO")]
    [InlineData("plain", "plain")]
    public void Mtext_codes_are_stripped(string raw, string plain) => Assert.Equal(plain, Extractor.PlainMText(raw));

    [Theory]
    [InlineData("*T12", "table_graphic")]
    [InlineData("HS_FRAME_A3", "title_block")]
    [InlineData("ANCHOR-M24", "anchor")]
    public void Classifies_like_extract_blocks_py(string name, string cat) => Assert.Equal(cat, Extractor.Classify(name));
}
