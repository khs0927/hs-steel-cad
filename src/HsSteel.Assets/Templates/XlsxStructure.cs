using System.IO.Compression;
using System.Text;
using System.Xml;

namespace HsSteel.Assets.Templates;

public sealed record SheetInfo(int Ord, string Name, string State, string? Dimension, int HeaderRow, IReadOnlyList<string> Headers, int RowCount, int ColCount, int FormulaCount);

public sealed record WorkbookInfo(bool HasVba, IReadOnlyList<string> VbaParts, IReadOnlyList<string> DefinedNames, IReadOnlyList<SheetInfo> Sheets);

/// <summary>
/// Minimal pure-C# reader of .xlsx/.xlsm structure: sheet names/state/dimension, the header row (first row in the top 30 with at least
/// 3 non-empty text cells) with its column headers, row/column extent and formula count. Cell values below the header are not kept.
/// </summary>
public static class XlsxStructure
{
    public static WorkbookInfo Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        ZipArchiveEntry? Entry(string name) => zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));

        var shared = new List<string>();
        if (Entry("xl/sharedStrings.xml") is { } ss)
        {
            using var r = XmlReader.Create(ss.Open(), Settings);
            var sb = new StringBuilder();
            var inSi = false;
            var more = r.Read();
            while (more)
            {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "si")
                {
                    inSi = true;
                    sb.Clear();
                }
                else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "si")
                {
                    inSi = false;
                    shared.Add(sb.ToString());
                }
                else if (inSi && r.NodeType == XmlNodeType.Element && r.LocalName == "t" && !r.IsEmptyElement)
                {
                    sb.Append(r.ReadElementContentAsString());
                    continue; // reader already advanced past </t>
                }

                more = r.Read();
            }
        }

        var rels = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Entry("xl/_rels/workbook.xml.rels") is { } re)
        {
            using var r = XmlReader.Create(re.Open(), Settings);
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship")
                {
                    var id = r.GetAttribute("Id");
                    var target = r.GetAttribute("Target");
                    if (id != null && target != null)
                    {
                        rels[id] = target.StartsWith('/') ? target[1..] : "xl/" + target;
                    }
                }
            }
        }

        var sheets = new List<(string Name, string State, string Part)>();
        var names = new List<string>();
        using (var r = XmlReader.Create((Entry("xl/workbook.xml") ?? throw new InvalidDataException("no xl/workbook.xml")).Open(), Settings))
        {
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (r.LocalName == "sheet")
                {
                    var rid = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                    sheets.Add((r.GetAttribute("name") ?? "", r.GetAttribute("state") ?? "visible", rid != null && rels.TryGetValue(rid, out var part) ? part : ""));
                }
                else if (r.LocalName == "definedName" && r.GetAttribute("name") is { } dn)
                {
                    names.Add(dn);
                }
            }
        }

        var result = new List<SheetInfo>();
        for (var i = 0; i < sheets.Count; i++)
        {
            var (name, state, part) = sheets[i];
            var entry = Entry(part);
            result.Add(entry is null
                ? new SheetInfo(i, name, state, null, 0, [], 0, 0, 0)
                : ReadSheet(i, name, state, entry, shared));
        }

        var vba = zip.Entries.Where(e => e.FullName.StartsWith("xl/vbaProject", StringComparison.OrdinalIgnoreCase)).Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        return new WorkbookInfo(vba.Count > 0, vba, names.OrderBy(n => n, StringComparer.Ordinal).ToList(), result);
    }

    private static readonly XmlReaderSettings Settings = new() { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true };

    private static SheetInfo ReadSheet(int ord, string name, string state, ZipArchiveEntry entry, List<string> shared)
    {
        string? dimension = null;
        var maxRow = 0;
        var maxCol = 0;
        var formulas = 0;
        var headerRow = 0;
        List<(int Col, string Text)>? header = null;
        var scanned = 0;

        using var r = XmlReader.Create(entry.Open(), Settings);
        var rowCells = new List<(int Col, string Text)>();
        var rowNo = 0;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                switch (r.LocalName)
                {
                    case "dimension":
                        dimension = r.GetAttribute("ref");
                        break;
                    case "row":
                        rowNo = int.TryParse(r.GetAttribute("r"), out var rn) ? rn : rowNo + 1;
                        maxRow = Math.Max(maxRow, rowNo);
                        rowCells.Clear();
                        break;
                    case "c":
                    {
                        var rref = r.GetAttribute("r") ?? "";
                        var type = r.GetAttribute("t");
                        var col = ColumnIndex(rref);
                        var text = string.Empty;
                        var hasValue = false;
                        if (!r.IsEmptyElement)
                        {
                            using var sub = r.ReadSubtree();
                            sub.Read();
                            while (!sub.EOF)
                            {
                                if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "f")
                                {
                                    formulas++;
                                    sub.Read();
                                }
                                else if (sub.NodeType == XmlNodeType.Element && sub.LocalName is "v" or "t")
                                {
                                    text += sub.ReadElementContentAsString();
                                    hasValue = true;
                                }
                                else
                                {
                                    sub.Read();
                                }
                            }
                        }

                        if (hasValue)
                        {
                            if (type == "s" && int.TryParse(text, out var si) && si >= 0 && si < shared.Count)
                            {
                                text = shared[si];
                            }

                            maxCol = Math.Max(maxCol, col);
                            if (rowNo <= 30 && type is "s" or "str" or "inlineStr")
                            {
                                rowCells.Add((col, text.Trim()));
                            }
                        }

                        break;
                    }
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "row")
            {
                scanned++;
                if (header is null && rowNo <= 30 && rowCells.Count(c => c.Text.Length > 0) >= 3)
                {
                    header = rowCells.Where(c => c.Text.Length > 0).ToList();
                    headerRow = rowNo;
                }
            }
        }

        _ = scanned;
        return new SheetInfo(ord, name, state, dimension, headerRow, header?.Select(h => h.Text).ToList() ?? [], maxRow, maxCol, formulas);
    }

    private static int ColumnIndex(string cellRef)
    {
        var n = 0;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                n = n * 26 + (ch - 'A' + 1);
            }
            else if (ch is >= 'a' and <= 'z')
            {
                n = n * 26 + (ch - 'a' + 1);
            }
            else
            {
                break;
            }
        }

        return n;
    }
}
