using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HsSteel.Modeling;

namespace HsSteel.Drafting;

/// <summary>One row of the assembly BOM (조립목록).</summary>
public sealed record BomAssemblyRow(string Mark, string Type, string MainSpec, double Length, int Qty, double UnitKg, double TotalKg, IReadOnlyList<string> Members);

/// <summary>One row of the material summary (자재집계표): shapes by spec, plates by thickness, or bolts by name.</summary>
public sealed record BomMaterialRow(string Spec, string Kind, string Quantity, double UnitWeight, double WeightKg, double PaintM2);

/// <summary>One row of the bolt schedule (볼트집계표).</summary>
public sealed record BomBoltRow(string Name, int Qty, int Assemblies);

/// <summary>
/// Structured bill of materials derived from a resolved model — same totals as drawing tables and <c>hs_bom</c>.
/// Korean shop titles: 조립목록 / 자재집계표 / 볼트집계표.
/// </summary>
public sealed class BomTable
{
    public required string Project { get; init; }

    public required IReadOnlyList<BomAssemblyRow> Assemblies { get; init; }

    public required IReadOnlyList<BomMaterialRow> Materials { get; init; }

    public required IReadOnlyList<BomBoltRow> Bolts { get; init; }

    public double TotalKg { get; init; }

    public int AssemblyQty => Assemblies.Sum(a => a.Qty);

    public static BomTable From(ModelResult model)
    {
        var assemblies = model.Assemblies
            .OrderBy(a => a.Type)
            .ThenBy(a => a.Mark, StringComparer.Ordinal)
            .Select(a => new BomAssemblyRow(
                a.Mark,
                a.Type.ToString().ToUpperInvariant(),
                a.Main.Profile.Spec,
                a.Main.Length,
                a.Quantity,
                a.Weight,
                Math.Round(a.Weight * a.Quantity, 1),
                a.Members.ToList()))
            .ToList();

        var materials = new List<BomMaterialRow>();
        foreach (var g in model.ShapeParts.GroupBy(p => p.Profile.Spec).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var lenM = g.Sum(p => p.Length * p.Quantity) / 1000;
            var wt = g.Sum(p => p.Weight * p.Quantity);
            var paint = g.Sum(p => p.PaintArea * p.Quantity);
            materials.Add(new BomMaterialRow(g.Key, g.First().Profile.Family, $"{lenM:0.00} m", g.First().Profile.UnitWeight, Math.Round(wt, 1), Math.Round(paint, 2)));
        }

        foreach (var g in model.PlateParts.GroupBy(p => p.Thickness).OrderBy(g => g.Key))
        {
            var area = g.Sum(p => p.SizeU * p.SizeV * p.Quantity) / 1e6;
            var wt = g.Sum(p => p.Weight * p.Quantity);
            materials.Add(new BomMaterialRow($"PL-{Annotate.F(g.Key)}", "PLATE", $"{area:0.00} m²", 0, Math.Round(wt, 1), Math.Round(area * 2, 2)));
        }

        var bolts = model.Assemblies
            .SelectMany(a => a.Bolts.Select(b => (Name: b.Label, Count: b.Count * a.Quantity, Assy: 1)))
            .GroupBy(b => b.Name)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new BomBoltRow(g.Key, g.Sum(x => x.Count), g.Sum(x => x.Assy)))
            .ToList();

        // Attach bolt rows to materials for the combined 자재집계표 (same as drawing sheet).
        foreach (var b in bolts)
        {
            materials.Add(new BomMaterialRow(b.Name, "BOLT", $"{b.Qty} EA", 0, 0, 0));
        }

        var total = Math.Round(
            model.ShapeParts.Sum(p => p.Weight * p.Quantity) + model.PlateParts.Sum(p => p.Weight * p.Quantity),
            1);

        return new BomTable
        {
            Project = model.Project.Name,
            Assemblies = assemblies,
            Materials = materials,
            Bolts = bolts,
            TotalKg = total,
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, BomJson);

    public static readonly JsonSerializerOptions BomJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>UTF-8 CSV with three sections: 조립목록, 자재집계표, 볼트집계표.</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {Project} BOM");
        sb.AppendLine("# 조립목록 (ASSEMBLY LIST)");
        sb.AppendLine("마크,종류,주부재,길이(mm),수량,단중(kg),중량(kg),부재ID");
        foreach (var a in Assemblies)
        {
            sb.AppendLine(string.Join(',',
                Csv(a.Mark), Csv(a.Type), Csv(a.MainSpec), F(a.Length), a.Qty.ToString(CultureInfo.InvariantCulture),
                F(a.UnitKg), F(a.TotalKg), Csv(string.Join(';', a.Members))));
        }

        sb.AppendLine($"합계,,,,,{(Assemblies.Sum(a => a.Qty)).ToString(CultureInfo.InvariantCulture)},,{F(TotalKg)}");
        sb.AppendLine();
        sb.AppendLine("# 자재집계표 (MATERIAL SUMMARY)");
        sb.AppendLine("규격,종류,수량,단중(kg/m),중량(kg),도장(m²)");
        foreach (var m in Materials.Where(m => m.Kind != "BOLT"))
        {
            sb.AppendLine(string.Join(',', Csv(m.Spec), Csv(m.Kind), Csv(m.Quantity), F(m.UnitWeight), F(m.WeightKg), F(m.PaintM2)));
        }

        sb.AppendLine($"합계,,,,{F(TotalKg)},");
        sb.AppendLine();
        sb.AppendLine("# 볼트집계표 (BOLT SCHEDULE)");
        sb.AppendLine("볼트,수량,조립수");
        foreach (var b in Bolts)
        {
            sb.AppendLine(string.Join(',', Csv(b.Name), b.Qty.ToString(CultureInfo.InvariantCulture), b.Assemblies.ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Csv(string s) =>
        s.Contains('"') || s.Contains(',') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : s;
}