using System.Globalization;
using System.Reflection;
using ACadSharp;
using ACadSharp.Header;
using ACadSharp.Tables;
using ACadSharp.IO;

namespace HsSteel.Assets.Templates;

/// <summary>One drafting-standard item of a template drawing: a layer, text style, dimension style, linetype, block, or header variable group.</summary>
public sealed record DraftingStyle(string Category, string Name, IReadOnlyDictionary<string, string> Props);

/// <summary>Reads layers, text styles, dimension styles, linetypes, user blocks and key header variables of a template DWG via ACadSharp.</summary>
public static class DwgStyleReader
{
    private static readonly HashSet<string> Skip = new(StringComparer.Ordinal)
    {
        "Handle", "Document", "Owner", "XDictionary", "ExtendedData", "Reactors", "ObjectType", "ObjectName", "SubclassMarker",
        "CadObject", "Name", "OwnerHandle", "DictionaryName", "Entities", "Layout", "BlockEntity", "BlockEnd", "Viewports", "Attributes",
        "Records", "Entries", "Template", "Reference", "Source", "Application", "SortEntitiesTable", "Strokes", "Segments",
    };

    public static IReadOnlyList<DraftingStyle> Read(string path)
    {
        CadDocument doc;
        using (var r = new DwgReader(path))
        {
            doc = r.Read();
        }

        var list = new List<DraftingStyle>();
        foreach (var l in doc.Layers.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            list.Add(new DraftingStyle("layer", l.Name, Reflect(l)));
        }

        foreach (var t in doc.TextStyles.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            list.Add(new DraftingStyle("text_style", t.Name, Reflect(t)));
        }

        foreach (var d in doc.DimensionStyles.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            list.Add(new DraftingStyle("dim_style", d.Name, Reflect(d)));
        }

        foreach (var lt in doc.LineTypes.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var props = Reflect(lt);
            props["segment_count"] = lt.Segments.Count().ToString(CultureInfo.InvariantCulture);
            list.Add(new DraftingStyle("linetype", lt.Name, props));
        }

        foreach (var b in doc.BlockRecords.Where(b => !b.Name.StartsWith('*')).OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var ents = b.Entities.ToList();
            list.Add(new DraftingStyle("block", b.Name, new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["entity_count"] = ents.Count.ToString(CultureInfo.InvariantCulture),
                ["entity_types"] = string.Join(",", ents.GroupBy(e => e.ObjectName).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + ":" + g.Count().ToString(CultureInfo.InvariantCulture))),
                ["layers"] = string.Join(",", ents.Select(e => e.Layer?.Name ?? string.Empty).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)),
                ["has_attributes"] = ents.Any(e => e is ACadSharp.Entities.AttributeDefinition) ? "true" : "false",
            }));
        }

        list.Add(new DraftingStyle("header", "units_and_scales", HeaderVars(doc.Header)));
        list.Add(new DraftingStyle("document", "summary", new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["version"] = doc.Header.Version.ToString(),
            ["model_space_entities"] = doc.ModelSpace.Entities.Count.ToString(CultureInfo.InvariantCulture),
            ["layout_count"] = doc.Layouts.Count().ToString(CultureInfo.InvariantCulture),
            ["layouts"] = string.Join(",", doc.Layouts.Select(l => l.Name).OrderBy(x => x, StringComparer.Ordinal)),
        }));
        return list;
    }

    private static readonly string[] HeaderNames =
    [
        "LinearUnitFormat", "LinearUnitPrecision", "AngularUnit", "AngularUnitPrecision", "UnitMode", "InsUnits", "LineTypeScale",
        "CurrentLayerName", "CurrentTextStyleName", "CurrentDimensionStyleName", "CurrentLineTypeName", "TextHeightDefault",
        "DimensionScaleFactor", "PaperSpaceLineTypeScaling", "PlotStyleMode", "FillMode", "LineWeightDisplay", "PointDisplayMode", "PointDisplaySize",
    ];

    private static SortedDictionary<string, string> HeaderVars(CadHeader h)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in HeaderNames)
        {
            var p = typeof(CadHeader).GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
            if (p?.GetIndexParameters().Length == 0 && Format(p.GetValue(h)) is { } v)
            {
                d[ToSnake(n)] = v;
            }
        }

        return d;
    }

    private static SortedDictionary<string, string> Reflect(object o)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0 || Skip.Contains(p.Name))
            {
                continue;
            }

            object? v;
            try
            {
                v = p.GetValue(o);
            }
            catch (Exception)
            {
                continue;
            }

            if (Format(v) is { } s)
            {
                d[ToSnake(p.Name)] = s;
            }
        }

        return d;
    }

    private static string? Format(object? v) => v switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        Enum e => e.ToString(),
        double x => double.IsFinite(x) ? x.ToString("0.######", CultureInfo.InvariantCulture) : null,
        float f => float.IsFinite(f) ? f.ToString("0.######", CultureInfo.InvariantCulture) : null,
        int or long or short or byte or ushort or uint or ulong => Convert.ToString(v, CultureInfo.InvariantCulture),
        Color c => c.IsByLayer ? "ByLayer" : c.IsByBlock ? "ByBlock" : c.IsTrueColor ? "rgb(" + c.R + "," + c.G + "," + c.B + ")" : "aci " + c.Index.ToString(CultureInfo.InvariantCulture),
        TableEntry te => te.Name,
        _ => null,
    };

    private static string ToSnake(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(s[i]));
        }

        return sb.ToString();
    }
}
