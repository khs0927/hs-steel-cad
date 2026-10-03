using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using HsSteel.Assets;
using HsSteel.Domain;
using HsSteel.Drafting;
using HsSteel.Modeling;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace HsSteel.Mcp;

/// <summary>
/// HS-Steel MCP tools: section data, project modelling, numbering/BOM and shop-drawing generation.
/// Projects are JSON files in the workspace; drawings are written as DWG/DXF (no AutoCAD needed), or returned
/// as power-cad draw plans (cad_create specs) for live creation in AutoCAD 2027.
/// </summary>
[McpServerToolType]
public sealed class HsTools(Workspace ws)
{
    private static readonly JsonSerializerOptions Out = new(Project.Json) { WriteIndented = false };

    private static string Json(object o) => JsonSerializer.Serialize(o, Out);

    private static McpException Fail(string message) => new(message);

    // ------------------------------------------------------------------ catalogue

    [McpServerTool(Name = "hs_catalog_validation", ReadOnly = true, Idempotent = true)]
    [Description("Validate the HS-STEEL section catalogue source files without modifying them. Returns PASS, FAIL or NOT_RUN plus file SHA-256, CP949 encoding, accepted/quarantined row counts and diagnostics.")]
    public string CatalogValidation() => Json(SectionCatalogValidation.ValidateDirectory(ws.AttributesDir));

    [McpServerTool(Name = "hs_section_search", ReadOnly = true, Idempotent = true)]
    [Description("Search the HS-STEEL section tables (H-BEAM, BH, ANGLE, CHANNEL, C-CHANNEL, SQ-PIPE, STEEL-PIPE, T-BAR, Z-BAR, FLAT-BAR, ROUND-BAR, PLATE, ...). "
        + "Returns spec, family, dimensions (mm), kg/m and paint m²/m.")]
    public string SectionSearch(
        [Description("Substring of the spec, e.g. 'H400' or 'L90x90'")] string query,
        [Description("Family (file name), e.g. 'H-BEAM'. Optional.")] string? family = null,
        int limit = 30)
    {
        var rows = ws.Catalog.Rows
            .Where(r => family is null || r.Family.Equals(family, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Spec.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(r => new { spec = r.Spec, family = r.Family, dims = r.M, kg_per_m = r.UnitWeight, paint_m2_per_m = r.PaintArea });
        return Json(rows);
    }

    [McpServerTool(Name = "hs_splice_standard", ReadOnly = true, Idempotent = true)]
    [Description("Standard bolted H splice (HS-STEEL SCSS tables) for a section: web/flange plates and bolt layout in HS-STEEL notation.")]
    public string SpliceStandard(string section, [Description("true = column splice (SCSS-C), false = girder (SCSS-G)")] bool column = false, [Description("16, 20 or 22; 0 = first available")] int boltSize = 0)
    {
        var s = ws.Splices.Find(section, column, boltSize) ?? throw Fail($"No SCSS standard for {section}.");
        return Json(new
        {
            standard = s.Standard, section = s.Section, web = s.WebNotation, flange = s.FlangeNotation, gap = s.Gap,
            web_bolt = s.WebBolt, web_bolt_count = s.WebBoltCount, flange_bolt = s.FlangeBolt, material = s.Material, raw = s.Raw,
        });
    }

    // ------------------------------------------------------------------ projects

    [McpServerTool(Name = "hs_project_list", ReadOnly = true)]
    [Description("List projects in the workspace.")]
    public string ProjectList() =>
        Json(Directory.Exists(ws.ProjectsDir)
            ? Directory.GetFiles(ws.ProjectsDir, "*.hsproj.json").Select(f => Path.GetFileName(f)[..^".hsproj.json".Length]).Order().ToArray()
            : []);

    [McpServerTool(Name = "hs_project_new")]
    [Description("Create an empty project (grids, levels, members and connections are added with the other tools).")]
    public string ProjectNew(string name, [Description("Date for title blocks, e.g. 2026.10.02")] string date = "", bool overwrite = false)
    {
        if (File.Exists(ws.PathOf(name)) && !overwrite)
        {
            throw Fail($"Project '{name}' exists; pass overwrite=true to replace it.");
        }

        var p = new Project { Name = name, Date = date };
        ws.Save(p);
        return Json(new { created = ws.PathOf(name) });
    }

    [McpServerTool(Name = "hs_project_frame")]
    [Description("Create a complete steel frame project from bay sizes: grids, levels, columns (base plates, caps, optional SCSS splices), "
        + "girders on every floor framed to columns with shear tabs, optional secondary beams framed to girders.")]
    public string ProjectFrame(
        string name,
        [Description("Bay widths along X in mm, e.g. [6000,6000]")] double[] spansX,
        [Description("Bay widths along Y in mm")] double[] spansY,
        [Description("Storey heights in mm from the base, e.g. [4500,4000]")] double[] storeys,
        string column = "H300x300x10x15",
        string girderX = "H400x200x8x13",
        string girderY = "H400x200x8x13",
        [Description("Secondary beams per X bay (0 = none)")] int subBeams = 0,
        string subBeam = "H300x150x6.5x9",
        [Description("Split columns longer than this (mm) with splices 1 m above a floor; 0 = no splices")] double maxColumnPiece = 0,
        string date = "",
        bool overwrite = false)
    {
        if (File.Exists(ws.PathOf(name)) && !overwrite)
        {
            throw Fail($"Project '{name}' exists; pass overwrite=true to replace it.");
        }

        var p = ProjectTemplates.Frame(name, new FrameSpec(spansX, spansY, storeys, column, girderX, girderY, maxColumnPiece, subBeams, subBeam));
        p.Date = date;
        ws.Save(p);
        return Summary(p, ws.Build(p));
    }

    [McpServerTool(Name = "hs_project_get", ReadOnly = true)]
    [Description("Return the full project JSON (grids, levels, members, connections).")]
    public string ProjectGet(string name) => ws.Load(name).ToJson();

    [McpServerTool(Name = "hs_project_put")]
    [Description("Replace a project with the given JSON (same schema as hs_project_get). Use for bulk edits.")]
    public string ProjectPut(string json)
    {
        var p = Project.FromJson(json);
        ws.Save(p);
        return Summary(p, ws.Build(p));
    }

    [McpServerTool(Name = "hs_grid_set")]
    [Description("Set the grid lines and levels of a project. Grid X lines are vertical (X = position), grid Y horizontal.")]
    public string GridSet(string name, [Description("[{\"name\":\"X1\",\"position\":0}, ...]")] GridLine[] gridX, GridLine[] gridY, [Description("[{\"name\":\"2F\",\"elevation\":4500}, ...] (optional)")] Level[]? levels = null)
    {
        var p = ws.Load(name);
        p.GridX = [.. gridX];
        p.GridY = [.. gridY];
        if (levels is not null)
        {
            p.Levels = [.. levels];
        }

        ws.Save(p);
        return Json(new { grid_x = p.GridX.Count, grid_y = p.GridY.Count, levels = p.Levels.Count });
    }

    [McpServerTool(Name = "hs_member_add")]
    [Description("Add or replace a member on its centre line. type: column, sub_column, post, girder, beam, crane_girder, brace, purlin, girth, rafter, truss, stair, hand_rail, embed, other. "
        + "Points are [x,y,z] in mm; grid names can be used instead of numbers via hs_project_put.")]
    public string MemberAdd(string name, string id, string type, string section, double[] start, double[] end, double roll = 0, string? material = null)
    {
        var p = ws.Load(name);
        var t = Enum.Parse<AssemblyType>(type.Replace("_", ""), ignoreCase: true);
        ws.Catalog.Resolve(section); // validates the spec
        p.Members.RemoveAll(m => m.Id == id);
        p.Members.Add(new MemberDef(id, t, section, new V3(start[0], start[1], start.Length > 2 ? start[2] : 0), new V3(end[0], end[1], end.Length > 2 ? end[2] : 0), roll, material));
        ws.Save(p);
        return Json(new { members = p.Members.Count });
    }

    [McpServerTool(Name = "hs_member_remove")]
    [Description("Remove a member and every connection that references it.")]
    public string MemberRemove(string name, string id)
    {
        var p = ws.Load(name);
        var n = p.Members.RemoveAll(m => m.Id == id);
        var c = p.Connections.RemoveAll(x => x switch
        {
            SpliceDef s => s.MemberA == id || s.MemberB == id,
            ShearTabDef t => t.Beam == id || t.Support == id,
            BasePlateDef b => b.Column == id,
            EndCapDef e => e.Member == id,
            _ => false,
        });
        ws.Save(p);
        return Json(new { removed_members = n, removed_connections = c });
    }

    [McpServerTool(Name = "hs_connection_add")]
    [Description("Add a connection. JSON with \"kind\": "
        + "splice {id, member_a, member_b, bolt_size} (member_a end meets member_b start; SCSS standard) | "
        + "shear_tab {id, beam, beam_end: start|end, support, plate_t, bolt_size} (plate welded to girder web or column face, beam web bolted) | "
        + "base_plate {id, column, thickness, margin, anchor_dia, anchor_edge} | end_cap {id, member, end: start|end, thickness}.")]
    public string ConnectionAdd(string name, string connectionJson)
    {
        var p = ws.Load(name);
        var c = JsonSerializer.Deserialize<ConnectionDef>(connectionJson, Project.Json) ?? throw Fail("Empty connection.");
        p.Connections.RemoveAll(x => x.Id == c.Id);
        p.Connections.Add(c);
        ws.Save(p);
        return Json(new { connections = p.Connections.Count });
    }

    // ------------------------------------------------------------------ model / quantities

    [McpServerTool(Name = "hs_model_build", ReadOnly = true)]
    [Description("Resolve the model: cut lengths, holes, plates, numbering (parts S#/P#, assemblies C#/G#/B#...), weights and warnings.")]
    public string ModelBuild(string name)
    {
        var p = ws.Load(name);
        return Summary(p, ws.Build(p));
    }

    [McpServerTool(Name = "hs_bom", ReadOnly = true)]
    [Description("Bill of materials: assemblies (qty, weight), parts (shape/plate with qty, length/size, weight), bolts, totals by spec.")]
    public string Bom(string name)
    {
        var r = ws.Build(ws.Load(name));
        return Json(new
        {
            assemblies = r.Assemblies.Select(a => new { mark = a.Mark, type = a.Type.ToString(), main = a.Main.Mark, qty = a.Quantity, unit_kg = a.Weight, members = a.Members }),
            shape_parts = r.ShapeParts.Select(s => new { mark = s.Mark, spec = s.Profile.Spec, length = s.Length, holes = s.Holes.Count, qty = s.Quantity, unit_kg = s.Weight, paint_m2 = s.PaintArea }),
            plate_parts = r.PlateParts.Select(pp => new { mark = pp.Mark, name = pp.Name, role = pp.Role, holes = pp.Holes.Count, qty = pp.Quantity, unit_kg = pp.Weight }),
            bolts = r.Assemblies.SelectMany(a => a.Bolts.Select(b => (b.Name, n: b.Count * a.Quantity))).GroupBy(b => b.Name).Select(g => new { name = g.Key, qty = g.Sum(x => x.n) }),
            by_spec = r.ShapeParts.GroupBy(s => s.Profile.Spec).Select(g => new { spec = g.Key, length_m = Math.Round(g.Sum(s => s.Length * s.Quantity) / 1000, 2), kg = Math.Round(g.Sum(s => s.Weight * s.Quantity), 1) }),
            total_kg = Math.Round(r.ShapeParts.Sum(s => s.Weight * s.Quantity) + r.PlateParts.Sum(pp => pp.Weight * pp.Quantity), 1),
            warnings = r.Warnings,
        });
    }

    // ------------------------------------------------------------------ drawings

    [McpServerTool(Name = "hs_frame_set")]
    [Description("Use the company sheet frame (도곽) for all drawings: a DWG/DXF file, optionally a block name inside it. "
        + "The drawing area is detected (largest inner rectangle) unless area=[x,y,w,h] (paper mm from the frame's lower-left) is given.")]
    public string FrameSet(string path, string? blockName = null, double[]? area = null)
    {
        ws.Frame = FrameSource.FromFile(path, blockName, area is { Length: 4 } ? (area[0], area[1], area[2], area[3]) : null);
        var f = ws.Frame;
        return Json(new { block = f.BlockName, width = f.Width, height = f.Height, area = new[] { f.AreaX, f.AreaY, f.AreaW, f.AreaH }, attribute_fields = f.AttributeTags });
    }

    [McpServerTool(Name = "hs_drawings_generate")]
    [Description("Generate shop drawings for a project into the company frame and write them as .dwg or .dxf (no AutoCAD needed; open the file in AutoCAD 2027). "
        + "kinds: any of assembly, part, plate, plan, bom (default all). separate=true writes one file per sheet into the output folder.")]
    public string DrawingsGenerate(string name, [Description("Output .dwg/.dxf path (or folder when separate=true)")] string output, string[]? kinds = null, bool separate = false)
    {
        var set = ws.Drawings(ws.Load(name), ParseKinds(kinds));
        var files = new List<string>();
        if (separate)
        {
            var ext = Path.GetExtension(output) is { Length: > 0 } e ? e : ".dwg";
            var dir = Path.HasExtension(output) ? Path.GetDirectoryName(Path.GetFullPath(output))! : output;
            foreach (var s in set.Sheets)
            {
                var f = Path.Combine(dir, s.Number + ext);
                DxfExporter.Write([s.Plan], ws.Frame, f);
                files.Add(f);
            }
        }
        else
        {
            DxfExporter.Write(set.Sheets, ws.Frame, output);
            files.Add(Path.GetFullPath(output));
        }

        return Json(new
        {
            files,
            sheets = set.Sheets.Select(s => new { dwg_no = s.Number, title = s.Title, scale = $"1/{s.Scale}", views = s.Plan.Meta["views"]?.AsArray().Count ?? 0 }),
            assemblies = set.Model.Assemblies.Count,
            warnings = set.Warnings,
        });
    }

    [McpServerTool(Name = "hs_draw_plan", ReadOnly = true)]
    [Description("Return one sheet (or one view) as a power-cad draw plan: entities in cad_create JSON plus HS-STEEL tags. "
        + "Feed the entities to power-cad (create_many) to draw them live in AutoCAD 2027. sheet = dwg number like 'A-001'; or kind+mark for a single view.")]
    public string DrawPlanOf(string name, string? sheet = null, [Description("assembly | part | plate")] string? kind = null, string? mark = null)
    {
        var p = ws.Load(name);
        if (sheet is not null)
        {
            var set = ws.Drawings(p, DrawingSet.Kinds.All);
            var s = set.Sheets.FirstOrDefault(x => x.Number == sheet) ?? throw Fail($"Sheet {sheet} not found.");
            return s.Plan.ToJson().ToJsonString();
        }

        var r = ws.Build(p);
        DrawPlan plan = kind switch
        {
            "assembly" => AssemblyDetail.Generate(r.Assemblies.FirstOrDefault(a => a.Mark == mark) ?? throw Fail($"Assembly {mark} not found.")),
            "part" => PartDetail.Generate(r.ShapeParts.FirstOrDefault(a => a.Mark == mark) ?? throw Fail($"Part {mark} not found.")),
            "plate" => PlateDetail.Generate(r.PlateParts.FirstOrDefault(a => a.Mark == mark) ?? throw Fail($"Plate {mark} not found.")),
            _ => throw Fail("Give sheet, or kind (assembly|part|plate) and mark."),
        };
        return plan.ToJson().ToJsonString();
    }

    public static DrawingSet.Kinds ParseKinds(string[]? kinds)
    {
        if (kinds is null || kinds.Length == 0)
        {
            return DrawingSet.Kinds.All;
        }

        DrawingSet.Kinds k = 0;
        foreach (var s in kinds)
        {
            k |= s.ToLowerInvariant() switch
            {
                "assembly" or "assy" => DrawingSet.Kinds.Assembly,
                "part" or "shape" => DrawingSet.Kinds.Part,
                "plate" => DrawingSet.Kinds.Plate,
                "plan" or "layout" or "erection" => DrawingSet.Kinds.Plan,
                "bom" => DrawingSet.Kinds.Bom,
                "all" => DrawingSet.Kinds.All,
                _ => throw new McpException($"Unknown kind '{s}'."),
            };
        }

        return k;
    }

    private static string Summary(Project p, ModelResult r) => Json(new
    {
        project = p.Name,
        members = p.Members.Count,
        connections = p.Connections.Count,
        assemblies = r.Assemblies.Select(a => $"{a.Mark} x{a.Quantity} ({a.Main.Profile.Spec} L={a.Main.Length}, {a.Weight} kg)"),
        shape_parts = r.ShapeParts.Count,
        plate_parts = r.PlateParts.Count,
        total_kg = Math.Round(r.Assemblies.Sum(a => a.Weight * a.Quantity), 1),
        warnings = r.Warnings,
    });
}
