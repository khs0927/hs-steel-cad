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

    private static V3 Point(string what, double[]? xyz)
    {
        if (xyz is null || xyz.Length < 2 || xyz.Length > 3 || !xyz.All(double.IsFinite))
        {
            throw Fail($"'{what}' must be [x,y] or [x,y,z] with finite numbers (mm).");
        }

        return new V3(xyz[0], xyz[1], xyz.Length > 2 ? xyz[2] : 0);
    }

    private static void RequireLengths(string what, double[]? values, bool allowEmpty)
    {
        if (values is null || (!allowEmpty && values.Length == 0))
        {
            throw Fail($"'{what}' needs at least one value (mm).");
        }

        if (!values.All(v => double.IsFinite(v) && v > 0))
        {
            throw Fail($"'{what}' values must be positive finite lengths in mm.");
        }
    }

    // ------------------------------------------------------------------ catalogue

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

    [McpServerTool(Name = "hs_section_catalog_handoff", ReadOnly = true, Idempotent = true)]
    [Description("Return one strict source-hashed section family as hs-steel-section-catalog/1. This proves only the selected family file parsed cleanly; it does not claim the whole legacy catalog is verified.")]
    public string SectionCatalogHandoff(
        [Description("Family/file stem, e.g. H-BEAM")] string family,
        [Description("Optional spec substring")] string? query = null,
        [Description("Maximum returned rows, 1-500")] int limit = 200)
    {
        var safeFamily = Path.GetFileNameWithoutExtension(family.Trim());
        if (string.IsNullOrWhiteSpace(safeFamily)
            || !string.Equals(safeFamily, family.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw Fail("family must be a file stem such as H-BEAM.");
        }

        var path = Path.Combine(ws.AttributesDir, safeFamily + ".dat");
        if (!File.Exists(path))
        {
            throw Fail($"Section family '{safeFamily}' is unavailable in the configured HS-STEEL assets.");
        }

        var report = SectionTable.LoadDetailed(path);
        if (!report.IsValid)
        {
            var first = report.Issues.FirstOrDefault();
            throw Fail(
                $"Section family '{safeFamily}' failed strict validation"
                + (first is null ? "." : $": {first.ErrorCode} at line {first.LineNumber}."));
        }

        return HsSteel.Assets.SectionCatalogHandoff.Build(report, query, limit).ToJsonString();
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
    public string ProjectNew(
        string name,
        [Description("Date for title blocks, e.g. 2026.10.02")] string date = "",
        bool overwrite = false,
        [Description("Bolt length table: by_bolt_set (default: TS grip+25/30/35, HTB grip+30/35/40) | kcs | ts_one_washer")] string boltLengthTable = "",
        [Description("Hole rule: standard (default) | oversize | legacy")] string holeRule = "",
        [Description("Mark scheme: legacy (default) | alt")] string markScheme = "",
        [Description("Mark format: plain (default) | floor_prefix")] string markFormat = "",
        [Description("Assembly mark number width: 3 = C001 (default), 2 = C01, 1 = C1; 0 = keep")] int markDigits = 0)
    {
        if (File.Exists(ws.PathOf(name)) && !overwrite)
        {
            throw Fail($"Project '{name}' exists; pass overwrite=true to replace it.");
        }

        var p = new Project { Name = name, Date = date, Rules = ApplyOptions(null, boltLengthTable, holeRule, markScheme, markFormat, markDigits) };
        ws.Save(p);
        return Json(new { created = ws.PathOf(name) });
    }

    [McpServerTool(Name = "hs_project_frame")]
    [Description("Create a complete steel frame from bay sizes: grids, levels, columns (base/caps/optional splices), "
        + "floor girders framed with shear tabs or end plates, optional secondary beams. "
        + "beam_connection: shear_tab|end_plate (전단접합|엔드플레이트). Optional detail rules: scallop/connection_gap/weld_gap/material.")]
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
        [Description("shear_tab | end_plate | 전단 | 엔드플레이트")] string beamConnection = "shear_tab",
        [Description("Flange scallop radius mm (0 = default 30)")] double scallop = 0,
        [Description("Beam-end clearance to support web mm (0 = default 10)")] double connectionGap = 0,
        [Description("Weld gap mm (0 = default 5)")] double weldGap = 0,
        [Description("Default material, e.g. SS275")] string material = "",
        string date = "",
        bool overwrite = false,
        [Description("Bolt length table: by_bolt_set (default: TS grip+25/30/35, HTB grip+30/35/40) | kcs | ts_one_washer")] string boltLengthTable = "",
        [Description("Hole rule: standard (default) | oversize | legacy")] string holeRule = "",
        [Description("Mark scheme: legacy (default) | alt")] string markScheme = "",
        [Description("Mark format: plain (default) | floor_prefix")] string markFormat = "",
        [Description("Assembly mark number width: 3 = C001 (default), 2 = C01, 1 = C1; 0 = keep")] int markDigits = 0)
    {
        if (File.Exists(ws.PathOf(name)) && !overwrite)
        {
            throw Fail($"Project '{name}' exists; pass overwrite=true to replace it.");
        }

        RequireLengths(nameof(spansX), spansX, allowEmpty: true);
        RequireLengths(nameof(spansY), spansY, allowEmpty: true);
        RequireLengths(nameof(storeys), storeys, allowEmpty: false);
        var conn = ParseBeamConnection(beamConnection);
        var rules = ApplyOptions(BuildRules(scallop, connectionGap, weldGap, material), boltLengthTable, holeRule, markScheme, markFormat, markDigits);
        var p = ProjectTemplates.Frame(name, new FrameSpec(spansX, spansY, storeys, column, girderX, girderY, maxColumnPiece, subBeams, subBeam, conn), rules);
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
        var p = Workspace.Parse(json);
        if (string.IsNullOrWhiteSpace(p.Name))
        {
            throw Fail("Project JSON needs a non-empty \"name\".");
        }

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

    [McpServerTool(Name = "hs_grid_from_bays")]
    [Description("Interactive grid helper: set X/Y grid lines (and optional levels) from bay/storey sizes with auto names X1..Xn, Y1..Yn, BASE/2F... "
        + "Creates the project if missing. Does not place members — use hs_project_frame for a full frame, or hs_member_add afterwards.")]
    public string GridFromBays(
        string name,
        [Description("Bay widths along X mm")] double[] spansX,
        [Description("Bay widths along Y mm")] double[] spansY,
        [Description("Optional storey heights mm from base")] double[]? storeys = null,
        string prefixX = "X",
        string prefixY = "Y",
        string date = "",
        bool overwriteGrids = true)
    {
        RequireLengths(nameof(spansX), spansX, allowEmpty: true);
        RequireLengths(nameof(spansY), spansY, allowEmpty: true);
        Project p;
        if (File.Exists(ws.PathOf(name)))
        {
            p = ws.Load(name);
        }
        else
        {
            p = new Project { Name = name, Date = date };
        }

        if (!overwriteGrids && (p.GridX.Count > 0 || p.GridY.Count > 0))
        {
            throw Fail($"Project '{name}' already has grids; pass overwriteGrids=true to replace them.");
        }

        p.GridX = BuildGrid(prefixX, spansX);
        p.GridY = BuildGrid(prefixY, spansY);
        if (storeys is { Length: > 0 })
        {
            RequireLengths(nameof(storeys), storeys, allowEmpty: false);
            p.Levels = BuildLevels(storeys);
        }

        if (string.IsNullOrWhiteSpace(p.Date) && !string.IsNullOrWhiteSpace(date))
        {
            p.Date = date;
        }

        ws.Save(p);
        return Json(new
        {
            project = p.Name,
            grid_x = p.GridX.Select(g => new { g.Name, g.Position }),
            grid_y = p.GridY.Select(g => new { g.Name, g.Position }),
            levels = p.Levels.Select(l => new { l.Name, l.Elevation }),
        });
    }

    [McpServerTool(Name = "hs_project_options")]
    [Description("Get or set the project standard options (stored in the project JSON): boltLengthTable by_bolt_set|kcs|ts_one_washer "
        + "(grip + add length, rounded up to 5 mm; by_bolt_set (default) = TS 1-washer 25/30/35/40, HTB KCS 14 31 25 table 2.1-5 30/35/40/45), "
        + "holeRule standard|oversize|legacy, markScheme legacy|alt (legacy embed head EB, alt AB), markFormat plain|floor_prefix, "
        + "markDigits 1..6 (3 = C001 default, 1 = C1). Omit all to just read (no change is written). Rebuild afterwards.")]
    public string ProjectOptions(
        string name,
        [Description("by_bolt_set (default) | kcs | ts_one_washer")] string boltLengthTable = "",
        [Description("standard (default) | oversize | legacy")] string holeRule = "",
        [Description("legacy (default) | alt")] string markScheme = "",
        [Description("plain (default) | floor_prefix")] string markFormat = "",
        [Description("Mark number width 1..6: 3 = C001 (default), 1 = C1; 0 = keep")] int markDigits = 0)
    {
        var p = ws.Load(name);
        var changed = !string.IsNullOrWhiteSpace(boltLengthTable) || !string.IsNullOrWhiteSpace(holeRule)
            || !string.IsNullOrWhiteSpace(markScheme) || !string.IsNullOrWhiteSpace(markFormat) || markDigits != 0;
        if (changed)
        {
            p.Rules = ApplyOptions(p.Rules, boltLengthTable, holeRule, markScheme, markFormat, markDigits);
            ws.Save(p);
        }

        var cur = p.Rules ?? ws.Rules;
        return Json(new
        {
            project = p.Name,
            changed,
            options = OptionsOf(cur),
            allowed = new
            {
                boltLengthTable = StandardOptions.BoltLengthTables,
                holeRule = StandardOptions.HoleRules,
                markScheme = StandardOptions.MarkSchemes,
                markFormat = StandardOptions.MarkFormats,
                markDigits = $"1..{StandardOptions.MaxMarkDigits}",
            },
        });
    }

    [McpServerTool(Name = "hs_project_rules")]
    [Description("Get or set fabrication DetailRules on a project (scallop/스캘럽, end gauge, hole dia, weld gap, connection gap, material). "
        + "Omit a field to keep the current/default value. Rules drive cope depth/radius and connection cuts on hs_model_build.")]
    public string ProjectRules(
        string name,
        double? scallop = null,
        double? endGauge = null,
        double? holeDia = null,
        double? weldGap = null,
        double? connectionGap = null,
        string? material = null)
    {
        var p = ws.Load(name);
        var cur = p.Rules ?? new DetailRules();
        var next = cur with
        {
            Scallop = scallop ?? cur.Scallop,
            EndGauge = endGauge ?? cur.EndGauge,
            HoleDia = holeDia ?? cur.HoleDia,
            WeldGap = weldGap ?? cur.WeldGap,
            ConnectionGap = connectionGap ?? cur.ConnectionGap,
            Material = string.IsNullOrWhiteSpace(material) ? cur.Material : material!,
        };
        p.Rules = next;
        ws.Save(p);
        return Json(new
        {
            project = p.Name,
            rules = new
            {
                scallop = next.Scallop,
                end_gauge = next.EndGauge,
                hole_dia = next.HoleDia,
                weld_gap = next.WeldGap,
                connection_gap = next.ConnectionGap,
                material = next.Material,
            },
        });
    }

    [McpServerTool(Name = "hs_member_add")]
    [Description("Add or replace a member on its centre line. type: column, sub_column, post, girder, beam, crane_girder, brace, purlin, girth, rafter, truss, stair, hand_rail, embed, other. "
        + "Points are [x,y,z] in mm; grid names can be used instead of numbers via hs_project_put.")]
    public string MemberAdd(string name, string id, string type, string section, double[] start, double[] end, double roll = 0, string? material = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw Fail("Member id must not be empty.");
        }

        // Enum.Parse alone also accepts numeric strings such as "99" and throws an ArgumentException
        // the client never sees; resolve against the defined names only.
        var key = (type ?? "").Replace("_", "").Trim();
        var typeName = Enum.GetNames<AssemblyType>().FirstOrDefault(n => string.Equals(n, key, StringComparison.OrdinalIgnoreCase))
            ?? throw Fail($"Unknown member type '{type}'. Use one of: {string.Join(", ", Enum.GetNames<AssemblyType>())}.");
        var t = Enum.Parse<AssemblyType>(typeName);

        try
        {
            ws.Catalog.Resolve(section); // validates the spec
        }
        catch (FormatException ex)
        {
            throw Fail(ex.Message);
        }

        var a = Point(nameof(start), start);
        var b = Point(nameof(end), end);
        if ((b - a).Length < 1e-6)
        {
            throw Fail($"Member '{id}' has zero length (start == end).");
        }

        var p = ws.Load(name);
        p.Members.RemoveAll(m => m.Id == id);
        p.Members.Add(new MemberDef(id, t, section, a, b, roll, material));
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
            EndPlateDef ep => ep.Beam == id || ep.Support == id,
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
        ConnectionDef? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ConnectionDef>(connectionJson, Project.Json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw Fail($"Connection JSON is not valid: {ex.Message}");
        }

        var c = parsed ?? throw Fail("Empty connection.");
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
    [Description("Bill of materials (물량): assemblies, parts, bolts, totals. Optional csv_path/json_path writes 조립목록·자재집계표·볼트집계표 export files.")]
    public string Bom(string name, string? csv_path = null, string? json_path = null)
    {
        var r = ws.Build(ws.Load(name));
        var bom = BomTable.From(r);
        if (!string.IsNullOrWhiteSpace(csv_path))
        {
            var csvFull = Path.GetFullPath(csv_path);
            var csvDir = Path.GetDirectoryName(csvFull);
            if (!string.IsNullOrEmpty(csvDir))
            {
                Directory.CreateDirectory(csvDir);
            }

            File.WriteAllText(csvFull, bom.ToCsv(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        if (!string.IsNullOrWhiteSpace(json_path))
        {
            var jsonFull = Path.GetFullPath(json_path);
            var jsonDir = Path.GetDirectoryName(jsonFull);
            if (!string.IsNullOrEmpty(jsonDir))
            {
                Directory.CreateDirectory(jsonDir);
            }

            File.WriteAllText(jsonFull, bom.ToJson(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return Json(new
        {
            assemblies = bom.Assemblies.Select(a => new { mark = a.Mark, type = a.Type, main_spec = a.MainSpec, length = a.Length, qty = a.Qty, unit_kg = a.UnitKg, total_kg = a.TotalKg, members = a.Members }),
            shape_parts = r.ShapeParts.Select(s => new { mark = s.Mark, spec = s.Profile.Spec, length = s.Length, holes = s.Holes.Count, qty = s.Quantity, unit_kg = s.Weight, paint_m2 = s.PaintArea }),
            plate_parts = r.PlateParts.Select(pp => new { mark = pp.Mark, name = pp.Name, role = pp.Role, holes = pp.Holes.Count, qty = pp.Quantity, unit_kg = pp.Weight }),
            bolts = bom.Bolts.Select(b => new { name = b.Name, qty = b.Qty, assemblies = b.Assemblies }),
            by_spec = r.ShapeParts.GroupBy(s => s.Profile.Spec).Select(g => new { spec = g.Key, length_m = Math.Round(g.Sum(s => s.Length * s.Quantity) / 1000, 2), kg = Math.Round(g.Sum(s => s.Weight * s.Quantity), 1) }),
            total_kg = bom.TotalKg,
            exports = new { csv = csv_path is null ? null : Path.GetFullPath(csv_path), json = json_path is null ? null : Path.GetFullPath(json_path) },
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
        + "kinds: any of assembly, part, plate, plan/elevation, bom (default all). separate=true writes one file per sheet into the output folder.")]
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
    [Description("Legacy HS-STEEL draw-plan JSON. Entity rows include an 'hs' tag field, so this payload is for inspection/backward compatibility and must not be sent directly to Power CAD cad_create.")]
    public string DrawPlanOf(string name, string? sheet = null, [Description("assembly | part | plate")] string? kind = null, string? mark = null) =>
        ResolveDrawPlan(name, sheet, kind, mark).ToJson().ToJsonString();

    [McpServerTool(Name = "hs_draw_plan_handoff", ReadOnly = true, Idempotent = true)]
    [Description("Return hs-steel-draw-plan/1 for Power CAD. Each row separates the strict cad_create 'spec' from the HS-STEEL 'tag'. The handoff is read-only provenance, never mutation authorization.")]
    public string DrawPlanHandoff(
        string name,
        string? sheet = null,
        [Description("assembly | part | plate")] string? kind = null,
        string? mark = null) =>
        ResolveDrawPlan(name, sheet, kind, mark).ToPowerCadHandoff().ToJsonString();

    private DrawPlan ResolveDrawPlan(string name, string? sheet, string? kind, string? mark)
    {
        var p = ws.Load(name);
        if (sheet is not null)
        {
            var set = ws.Drawings(p, DrawingSet.Kinds.All);
            return set.Sheets.FirstOrDefault(x => x.Number == sheet)?.Plan
                ?? throw Fail($"Sheet {sheet} not found.");
        }

        var r = ws.Build(p);
        return kind switch
        {
            "assembly" => AssemblyDetail.Generate(r.Assemblies.FirstOrDefault(a => a.Mark == mark) ?? throw Fail($"Assembly {mark} not found.")),
            "part" => PartDetail.Generate(r.ShapeParts.FirstOrDefault(a => a.Mark == mark) ?? throw Fail($"Part {mark} not found.")),
            "plate" => PlateDetail.Generate(r.PlateParts.FirstOrDefault(a => a.Mark == mark) ?? throw Fail($"Plate {mark} not found.")),
            _ => throw Fail("Give sheet, or kind (assembly|part|plate) and mark."),
        };
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
                "plan" or "layout" or "erection" or "elevation" or "입면" => DrawingSet.Kinds.Plan,
                "bom" => DrawingSet.Kinds.Bom,
                "all" => DrawingSet.Kinds.All,
                "cover" or "표지" => DrawingSet.Kinds.Cover,
                "notes" or "일반사항" => DrawingSet.Kinds.Notes,
                "tables" or "용접표" or "볼트표" => DrawingSet.Kinds.Tables,
                "anchor" or "앵커" => DrawingSet.Kinds.Anchor,
                "docs" => DrawingSet.Kinds.Docs,
                "full" => DrawingSet.Kinds.Full,
                _ => throw new McpException($"Unknown kind '{s}'."),
            };
        }

        return k;
    }

    private static BeamConnectionKind ParseBeamConnection(string? value)
    {
        var key = (value ?? "shear_tab").Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "");
        return key switch
        {
            "shear_tab" or "sheartab" or "shear" or "gusset" or "전단" or "전단접합" or "1면마찰" => BeamConnectionKind.ShearTab,
            "end_plate" or "endplate" or "end" or "엔드플레이트" or "엔드" => BeamConnectionKind.EndPlate,
            _ => throw Fail($"Unknown beam_connection '{value}'. Use shear_tab or end_plate."),
        };
    }

    private static object OptionsOf(DetailRules r) => new
    {
        boltLengthTable = StandardOptions.NormalizeBoltLengthTable(r.BoltLengthTable),
        holeRule = StandardOptions.NormalizeHoleRule(r.HoleRule),
        markScheme = StandardOptions.NormalizeMarkScheme(r.MarkScheme),
        markFormat = StandardOptions.NormalizeMarkFormat(r.MarkFormat),
        markDigits = StandardOptions.NormalizeMarkDigits(r.MarkDigits),
    };

    private static string PickOption(string? value, string[] allowed, string what, string current)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return current;
        }

        if (!StandardOptions.IsValid(value, allowed))
        {
            throw Fail($"{what} must be one of: {string.Join(", ", allowed)} (got '{value}').");
        }

        return value.Trim().ToLowerInvariant().Replace('-', '_');
    }

    /// <summary>Standard options onto <paramref name="rules"/> (null = workspace rules); returns <paramref name="rules"/> unchanged when no option is given.</summary>
    private DetailRules? ApplyOptions(DetailRules? rules, string? boltLengthTable, string? holeRule, string? markScheme, string? markFormat, int markDigits = 0)
    {
        if (string.IsNullOrWhiteSpace(boltLengthTable) && string.IsNullOrWhiteSpace(holeRule)
            && string.IsNullOrWhiteSpace(markScheme) && string.IsNullOrWhiteSpace(markFormat) && markDigits == 0)
        {
            return rules;
        }

        if (markDigits < 0 || markDigits > StandardOptions.MaxMarkDigits)
        {
            throw Fail($"markDigits must be 1..{StandardOptions.MaxMarkDigits} (got {markDigits}).");
        }

        var cur = rules ?? ws.Rules;
        return cur with
        {
            BoltLengthTable = PickOption(boltLengthTable, StandardOptions.BoltLengthTables, nameof(boltLengthTable), cur.BoltLengthTable),
            HoleRule = PickOption(holeRule, StandardOptions.HoleRules, nameof(holeRule), cur.HoleRule),
            MarkScheme = PickOption(markScheme, StandardOptions.MarkSchemes, nameof(markScheme), cur.MarkScheme),
            MarkFormat = PickOption(markFormat, StandardOptions.MarkFormats, nameof(markFormat), cur.MarkFormat),
            MarkDigits = markDigits == 0 ? cur.MarkDigits : markDigits,
        };
    }

    private static DetailRules? BuildRules(double scallop, double connectionGap, double weldGap, string material)
    {
        if (scallop <= 0 && connectionGap <= 0 && weldGap <= 0 && string.IsNullOrWhiteSpace(material))
        {
            return null;
        }

        var d = new DetailRules();
        return d with
        {
            Scallop = scallop > 0 ? scallop : d.Scallop,
            ConnectionGap = connectionGap > 0 ? connectionGap : d.ConnectionGap,
            WeldGap = weldGap > 0 ? weldGap : d.WeldGap,
            Material = string.IsNullOrWhiteSpace(material) ? d.Material : material,
        };
    }

    private static List<GridLine> BuildGrid(string prefix, double[] spans)
    {
        var lines = new List<GridLine> { new($"{prefix}1", 0) };
        double pos = 0;
        for (var i = 0; i < spans.Length; i++)
        {
            pos += spans[i];
            lines.Add(new GridLine($"{prefix}{i + 2}", pos));
        }

        return lines;
    }

    private static List<Level> BuildLevels(double[] storeys)
    {
        var levels = new List<Level> { new("BASE", 0) };
        double z = 0;
        for (var i = 0; i < storeys.Length; i++)
        {
            z += storeys[i];
            levels.Add(new Level($"{i + 2}F", z));
        }

        return levels;
    }

    private static string Summary(Project p, ModelResult r) => Json(new
    {
        project = p.Name,
        members = p.Members.Count,
        connections = p.Connections.Count,
        beam_connection = p.Connections.OfType<EndPlateDef>().Any() ? "end_plate" : p.Connections.OfType<ShearTabDef>().Any() ? "shear_tab" : null,
        rules = p.Rules is null ? null : new { scallop = p.Rules.Scallop, connection_gap = p.Rules.ConnectionGap, weld_gap = p.Rules.WeldGap, material = p.Rules.Material, options = OptionsOf(p.Rules) },
        assemblies = r.Assemblies.Select(a => $"{a.Mark} x{a.Quantity} ({a.Main.Profile.Spec} L={a.Main.Length}, {a.Weight} kg)"),
        shape_parts = r.ShapeParts.Count,
        plate_parts = r.PlateParts.Count,
        total_kg = Math.Round(r.Assemblies.Sum(a => a.Weight * a.Quantity), 1),
        warnings = r.Warnings,
    });
}
