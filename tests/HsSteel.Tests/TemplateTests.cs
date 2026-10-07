using System.IO.Compression;
using System.Text;
using HsSteel.Assets.Templates;

namespace HsSteel.Tests;

public class TemplateTests
{
    private const string Template = @"C:\HS-STEEL\HSSTEEL\새공사-설치용";

    [Fact]
    public void ParsesAlistSettingsAndRows()
    {
        var f = DatFiles.Parse("000000000-201809131045\n(\"WDGAP\" 5)\n(\"PROJECT\" \"00공장 신축\")\n(\"currentpaper\" . \"A3\")\n(\"MARK\" \"M1\" M2 3 \"x y\")\n(\"FLAG\")\n");
        Assert.Equal(DatFiles.Alist, f.Shape);
        Assert.Equal("000000000-201809131045", f.Header);
        Assert.Equal(5, f.Entries.Count);
        Assert.Equal(("WDGAP", "5", "number"), (f.Entries[0].Key, f.Entries[0].Value, f.Entries[0].ValueKind));
        Assert.Equal("00공장 신축", f.Entries[1].Value);
        Assert.Equal(("currentpaper", "A3"), (f.Entries[2].Key, f.Entries[2].Value));
        Assert.Equal(["M1", "M2", "3", "x y"], f.Entries[3].Atoms);
        Assert.Null(f.Entries[3].Value);
        Assert.Equal("flag", f.Entries[4].ValueKind);
    }

    [Fact]
    public void ParsesLineList()
    {
        var f = DatFiles.Parse("Base_H350\r\n28\r\n175\r\nAB M20(L-700)\r\n");
        Assert.Equal(DatFiles.LineList, f.Shape);
        Assert.Equal("Base_H350", f.Name);
        Assert.Equal(["28", "175", "AB M20(L-700)"], f.Lines);
    }

    [Fact]
    public void DecodeFallsBackToCp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Assert.Equal("한글", DatFiles.Decode(Encoding.GetEncoding(949).GetBytes("한글")));
        Assert.Equal("한글", DatFiles.Decode(new UTF8Encoding(false).GetBytes("한글")));
    }

    [Fact]
    public void XlsxStructureReadsSheetsHeadersAndVba()
    {
        var path = Path.Combine(Path.GetTempPath(), "hs_xlsx_" + Guid.NewGuid().ToString("N") + ".xlsm");
        try
        {
            using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Add(string name, string xml)
                {
                    using var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false));
                    w.Write(xml);
                }

                Add("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Hidden\" sheetId=\"2\" state=\"hidden\" r:id=\"rId2\"/></sheets>" +
                    "<definedNames><definedName name=\"Rng\">Data!$A$1</definedName></definedNames></workbook>");
                Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
                Add("xl/sharedStrings.xml", "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>Mark</t></si><si><t>Spec</t></si><si><t>Qty</t></si><si><t>Title</t></si></sst>");
                Add("xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><dimension ref=\"A1:C3\"/><sheetData>" +
                    "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>3</v></c></row>" +
                    "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>0</v></c><c r=\"B2\" t=\"s\"><v>1</v></c><c r=\"C2\" t=\"s\"><v>2</v></c></row>" +
                    "<row r=\"3\"><c r=\"A3\"><v>1</v></c><c r=\"B3\"><f>A3*2</f><v>2</v></c></row></sheetData></worksheet>");
                Add("xl/worksheets/sheet2.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData/></worksheet>");
                Add("xl/vbaProject.bin", "x");
            }

            var wb = XlsxStructure.Read(path);
            Assert.True(wb.HasVba);
            Assert.Equal(["Rng"], wb.DefinedNames);
            Assert.Equal(["Data", "Hidden"], wb.Sheets.Select(s => s.Name));
            Assert.Equal("hidden", wb.Sheets[1].State);
            var s0 = wb.Sheets[0];
            Assert.Equal(("A1:C3", 2), (s0.Dimension, s0.HeaderRow));
            Assert.Equal(["Mark", "Spec", "Qty"], s0.Headers);
            Assert.Equal((3, 3, 1), (s0.RowCount, s0.ColCount, s0.FormulaCount));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyTemplateWorkbookAndDrawingAreReadable()
    {
        if (!Directory.Exists(Template))
        {
            return;
        }

        var wb = XlsxStructure.Read(Path.Combine(Template, "BOM자재산출서.xlsm"));
        Assert.Contains(wb.Sheets, s => s.Name == "자재산출서");
        Assert.True(wb.Sheets.All(s => s.Name.Length > 0));

        var styles = DwgStyleReader.Read(Path.Combine(Template, "새공사-2019.dwg"));
        Assert.Contains(styles, s => s.Category == "layer");
        Assert.Contains(styles, s => s.Category == "text_style");
        Assert.Contains(styles, s => s.Category == "dim_style");
        Assert.Contains(styles, s => s.Category == "header");
        Assert.Equal(styles.Select(s => (s.Category, s.Name)), DwgStyleReader.Read(Path.Combine(Template, "새공사-2019.dwg")).Select(s => (s.Category, s.Name)));

        var project = DatFiles.Parse(DatFiles.Decode(File.ReadAllBytes(Path.Combine(Template, "attributes", "Project.dat"))));
        Assert.Contains(project.Entries, e => e.Key == "WDGAP" && e.Value == "5");
        Assert.Contains(project.Entries, e => e.Key == "PROJECT");
    }
}
