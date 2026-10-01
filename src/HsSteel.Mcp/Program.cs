using System.ComponentModel;
using System.Text.Json.Nodes;
using HsSteel.Assets;
using HsSteel.Domain;
using HsSteel.Drafting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Standalone MCP host for development. After the merge these tools move into power-cad-server
// (same ModelContextProtocol 2.2 attributes) and gain the "autocad" target through its pipe.
if (args.Length >= 2 && args[0] == "--demo")
{
    var plan = HsTools.Demo();
    DxfExporter.Write([plan], SheetFrame.A3Default, args[1]);
    Console.WriteLine($"wrote {args[1]}: {plan.Entities.Count} entities, scale 1/{plan.Scale}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<HsTools>();
await builder.Build().RunAsync();

[McpServerToolType]
public sealed class HsTools
{
    private static readonly string Legacy = Environment.GetEnvironmentVariable("HS_STEEL_LEGACY") ?? @"C:\HS-STEEL\HSSTEEL";

    private static IEnumerable<SectionRecord> AllSections() =>
        Directory.Exists(Path.Combine(Legacy, "attributes"))
            ? Directory.GetFiles(Path.Combine(Legacy, "attributes"), "*.dat")
                .Where(f => !f.EndsWith("Project.dat", StringComparison.OrdinalIgnoreCase))
                .SelectMany(SectionTable.Load)
            : [];

    private static DetailRules Rules()
    {
        var p = Path.Combine(Legacy, "attributes", "Project.dat");
        return File.Exists(p) ? DetailRules.From(ProjectSettings.Load(p)) : new DetailRules();
    }

    [McpServerTool(Name = "hs_section_search", ReadOnly = true, Idempotent = true)]
    [Description("Search HS-STEEL section tables (H-BEAM, ANGLE, CHANNEL, SQ-PIPE, ...). Returns spec, dims (mm), kg/m, paint m²/m.")]
    public static string SectionSearch(
        [Description("Substring of the spec, e.g. 'H300' or 'L50x50'")] string query,
        [Description("Family file name, e.g. 'H-BEAM' (optional)")] string? family = null,
        int limit = 20)
    {
        var rows = AllSections()
            .Where(r => family is null || r.Family.Equals(family, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Spec.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(r => new JsonObject
            {
                ["spec"] = r.Spec, ["family"] = r.Family, ["dims"] = new JsonArray([.. r.M.Select(v => (JsonNode)v)]),
                ["kg_per_m"] = r.UnitWeight, ["paint_m2_per_m"] = r.PaintArea,
            });
        return new JsonArray([.. rows]).ToJsonString();
    }

    [McpServerTool(Name = "hs_draw_member_detail")]
    [Description("Generate a single-part shop detail (elevation, plan, section, holes, scallops, true dimensions) for a straight H member, "
        + "placed in the sheet frame. Returns the draw plan (power-cad cad_create JSON). With dxf_path, also writes a DXF (no AutoCAD needed).")]
    public static string DrawMemberDetail(
        [Description("Member mark, e.g. 'B1'")] string mark,
        [Description("H section spec, e.g. 'H400x200x8x13'")] string spec,
        [Description("Cut length in mm")] double length,
        int quantity = 1,
        string material = "SS275",
        [Description("Web bolt group at both ends: rows along the member")] int webRows = 0,
        double webPitch = 70,
        [Description("Bolt lines across the web")] int webLines = 2,
        double webGauge = 60,
        bool scallop = false,
        [Description("Optional output .dxf path")] string? dxfPath = null)
    {
        var rules = Rules();
        var row = AllSections().FirstOrDefault(r => r.Spec.Equals(spec, StringComparison.OrdinalIgnoreCase));
        var section = row is null ? HSection.Parse(spec) : HSection.From(row);
        List<(MemberEnd, BoltGroup)> holes = [];
        if (webRows > 0)
        {
            var g = new BoltGroup("web", rules.EndGauge, webRows, webPitch, webLines, webGauge, rules.HoleDia);
            holes = [(MemberEnd.Start, g), (MemberEnd.End, g)];
        }

        var plan = new MemberDetailGenerator(rules, SheetFrame.A3Default)
            .Generate(new Member(mark, section, length, quantity, material, holes, scallop));
        if (dxfPath is not null)
        {
            DxfExporter.Write([plan], SheetFrame.A3Default, dxfPath);
            plan.Meta["dxf"] = dxfPath;
        }

        return plan.ToJsonString();
    }

    public static DrawPlan Demo()
    {
        var g = new BoltGroup("web", 40, 3, 70, 2, 60, 22);
        var m = new Member("B1", HSection.Parse("H400x200x8x13"), 9000, 4, "SS275", [(MemberEnd.Start, g), (MemberEnd.End, g)], true);
        return new MemberDetailGenerator(Rules(), SheetFrame.A3Default).Generate(m);
    }
}
