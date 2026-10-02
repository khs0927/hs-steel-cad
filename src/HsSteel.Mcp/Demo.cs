using HsSteel.Drafting;
using HsSteel.Modeling;

namespace HsSteel.Mcp;

/// <summary>Generates the sample project (2x1 bays, 2 storeys, spliced columns, secondary beams) end to end.</summary>
public static class Demo
{
    public static Project Sample()
    {
        var p = ProjectTemplates.Frame("SAMPLE-FRAME", new FrameSpec([6000, 6000], [8000], [4500, 4000], MaxColumnPiece: 6000, SubBeams: 1));
        p.Date = "2026.10.02";
        return p;
    }

    public static string Run(string output)
    {
        var ws = Workspace.FromEnvironment();
        var p = Sample();
        var set = DrawingSet.Generate(p, ws.Catalog, ws.Splices, ws.Frame);
        if (Path.HasExtension(output))
        {
            DxfExporter.Write(set.Sheets, ws.Frame, output);
        }
        else
        {
            foreach (var sheet in set.Sheets)
            {
                DxfExporter.Write([sheet.Plan], ws.Frame, Path.Combine(output, sheet.Number + ".dxf"));
            }
        }
        var lines = new List<string>
        {
            $"wrote {output}: {set.Sheets.Count} sheets, {set.Model.Assemblies.Count} assemblies, {set.Model.ShapeParts.Count} shape parts, {set.Model.PlateParts.Count} plates",
        };
        lines.AddRange(set.Sheets.Select(s => $"  {s.Number}  1/{s.Scale}  {s.Title}  ({s.Plan.Entities.Count} entities)"));
        lines.AddRange(set.Warnings.Select(w => "  ! " + w));
        return string.Join(Environment.NewLine, lines);
    }
}
