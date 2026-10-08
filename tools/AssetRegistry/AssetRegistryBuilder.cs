using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HsSteel.Domain;
using HsSteel.Drafting;
using HsSteel.Modeling;

namespace HsSteel.AssetRegistry;

/// <summary>
/// Builds the machine-readable asset registry (<c>hs-steel-asset-registry/1</c>) from what is in the repository:
/// the legacy file inventory <c>assets/manifest.json</c> (the licensed HS-STEEL files themselves are not in git) and the
/// code-defined drafting/domain standards (layers, styles, frame, generators, connection kinds, options, tables).
/// The output is deterministic (no timestamps, ordinal sort) so <c>--check</c> can detect drift.
/// </summary>
public static partial class AssetRegistryBuilder
{
    public const string Schema = "hs-steel-asset-registry/1";
    public const string RegistryRel = "assets/registry/asset-registry.json";
    public const string SchemaRel = "assets/registry/asset-registry.schema.json";
    public const string ManifestRel = "assets/manifest.json";
    public const string RulesRel = "src/HsSteel.Knowledge/Rules/rules.json";

    private static readonly JsonSerializerOptions Out = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Category id → description, in output order. Consumers must tolerate unknown categories.</summary>
    public static readonly (string Id, string Description)[] Categories =
    [
        ("block", "Legacy HS-STEEL detail block DWGs (HSSTEEL/block/*.dwg): frames, tables, notes, marks, anchors, lugs..."),
        ("section-table", "Legacy section/plate/deck/grating tables (HSSTEEL/attributes/*.dat)"),
        ("connection-table", "Legacy SCSS bolted splice tables (HSSTEEL/attributes/SCSS-*.dat)"),
        ("project-defaults", "Legacy Project.dat default fabrication settings"),
        ("project-template", "Legacy new-project set (HSSTEEL/새공사-설치용): dat, template dwg, BOM workbook, plot style, scripts"),
        ("linetype", "Legacy linetype / multiline style files (*.lin, *.mln)"),
        ("font-map", "Legacy font mapping (*.fmp)"),
        ("command-alias", "Legacy command alias file (*.pgp)"),
        ("dialog", "Legacy dialog definitions (*.DCL, *.xpg)"),
        ("tool-palette", "Legacy tool palette definitions (*.atc, *.xtp)"),
        ("quantity-workbook", "Legacy quantity/estimate workbooks (*.xlsx, *.xlsm)"),
        ("slide", "Legacy section/detail preview slides (*.sld)"),
        ("icon-set", "Legacy ribbon/toolbar/palette icons, one asset per folder (props.members lists the files)"),
        ("command-catalog", "Legacy menus/ribbons and compiled plugins (reference only: command catalog)"),
        ("plot-spec", "Legacy plot styles and batch-plot tool (reference only)"),
        ("reference-doc", "Legacy manuals and memos (reference only)"),
        ("golden-reference", "Real legacy project sets used as regression oracles (reference only)"),
        ("layer", "Layer standard of generated shop drawings (HsSteel.Drafting.Layers)"),
        ("text-style", "Text style definitions created in the target drawing when missing (HsSteel.Drafting.TextStyles)"),
        ("dim-style", "Dimension style definitions created in the target drawing when missing (HsSteel.Drafting.TextStyles)"),
        ("title-block", "Sheet frame (도곽) model (HsSteel.Drafting.SheetFrame); the company frame replaces it via hs_frame_set"),
        ("drawing-generator", "Shop-drawing sheet generators (HsSteel.Drafting) and the drawing set composer"),
        ("annotation-standard", "Callouts, weld symbols, bolt notes and dimension helpers (HsSteel.Drafting)"),
        ("data-table", "Code data tables: scales, weld/bolt tables, assembly mark heads, paper constants"),
        ("standard-option", "Project standard options and fabrication detail rules (HsSteel.Domain)"),
        ("rules-catalog", "Evidence-backed detailing rules catalog (rules.json / docs/RULES_CATALOG.md)"),
        ("profile-family", "Cross-section shape kinds supported by the drawing engine (HsSteel.Domain.ShapeKind)"),
        ("connection-detail", "Connection kinds of the project model (HsSteel.Modeling ConnectionDef subtypes)"),
        ("model-template", "Parametric project model templates (HsSteel.Modeling.ProjectTemplates)"),
    ];

    /// <summary>Walks up from the current directory (then the app base) to the folder holding HsSteel.sln.</summary>
    public static string FindRepoRoot(string? start = null)
    {
        foreach (var s in new[] { start, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = s is null ? null : new DirectoryInfo(s); d is not null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "HsSteel.sln")) && File.Exists(Path.Combine(d.FullName, ManifestRel)))
                {
                    return d.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("hs-steel-cad repository root (HsSteel.sln + assets/manifest.json) not found; pass --repo <root>.");
    }

    public static string Serialize(JsonObject doc) => doc.ToJsonString(Out).ReplaceLineEndings("\n") + "\n";

    /// <summary>CRLF -> LF so the manifest hash does not depend on git autocrlf.</summary>
    private static byte[] NormalizeEol(byte[] bytes) =>
        System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n"));

    public static JsonObject Build(string repoRoot)
    {
        var manifestPath = Path.Combine(repoRoot, ManifestRel);
        var manifestBytes = File.ReadAllBytes(manifestPath);
        using var manifest = JsonDocument.Parse(manifestBytes);
        var assets = new List<JsonObject>();
        assets.AddRange(LegacyAssets(manifest.RootElement));
        assets.AddRange(CodeAssets(repoRoot));
        assets.Sort((a, b) => string.CompareOrdinal(a["id"]!.GetValue<string>(), b["id"]!.GetValue<string>()));

        var dup = assets.GroupBy(a => a["id"]!.GetValue<string>(), StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            throw new InvalidOperationException($"Duplicate asset id '{dup.Key}'.");
        }

        var byCategory = new JsonObject();
        var categories = new JsonArray();
        foreach (var (id, description) in Categories)
        {
            var n = assets.Count(a => a["category"]!.GetValue<string>() == id);
            byCategory[id] = n;
            categories.Add(new JsonObject { ["id"] = id, ["description"] = description, ["count"] = n });
        }

        return new JsonObject
        {
            ["$schema"] = "./asset-registry.schema.json",
            ["schema"] = Schema,
            ["generator"] = "tools/AssetRegistry",
            ["regenerate"] = "dotnet run --project tools/AssetRegistry",
            ["check"] = "dotnet run --project tools/AssetRegistry -- --check",
            ["roots"] = new JsonObject
            {
                ["repo"] = new JsonObject { ["description"] = "hs-steel-cad repository root; source.path is relative to it" },
                ["legacy"] = new JsonObject
                {
                    ["description"] = "Licensed HS-STEEL install tree (not in git); source.path is relative to it, same as assets/manifest.json relPath",
                    ["env"] = "HS_STEEL_LEGACY",
                    ["envDefault"] = @"C:\HS-STEEL\HSSTEEL",
                    ["rule"] = "legacy root = parent directory of HS_STEEL_LEGACY (default C:\\HS-STEEL)",
                },
            },
            ["sourceManifest"] = new JsonObject
            {
                ["path"] = ManifestRel,
                ["schema"] = manifest.RootElement.GetProperty("schema").GetString(),
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(NormalizeEol(manifestBytes))), // EOL-normalised: identical on LF and CRLF (Windows autocrlf) checkouts
                ["fileCount"] = manifest.RootElement.GetProperty("fileCount").GetInt32(),
            },
            ["categories"] = categories,
            ["counts"] = new JsonObject { ["total"] = assets.Count, ["byCategory"] = byCategory },
            ["assets"] = new JsonArray([.. assets]),
        };
    }

    // ------------------------------------------------------------------ JSON helpers

    private static JsonArray Arr(params string[] items) => new([.. items.Select(i => (JsonNode)JsonValue.Create(i))]);

    private static JsonObject Source(string root, string path, string? symbol = null, string? sha256 = null, long? size = null) => new()
    {
        ["root"] = root, ["path"] = path, ["symbol"] = symbol, ["sha256"] = sha256, ["size"] = size,
    };

    private static JsonObject Use(string kind, string reference, string? note = null) => new() { ["kind"] = kind, ["ref"] = reference, ["note"] = note };

    private static JsonObject Param(string name, string type, string? unit = null, JsonNode? def = null, bool required = false, IEnumerable<string>? values = null, string? description = null) => new()
    {
        ["name"] = name, ["type"] = type, ["unit"] = unit, ["default"] = def, ["required"] = required,
        ["enum"] = values is null ? null : Arr([.. values]), ["description"] = description,
    };

    private static JsonObject Insertion(double[]? basePoint, string basePointSource, string? scaleRule) => new()
    {
        ["basePoint"] = basePoint is null ? null : new JsonArray([.. basePoint.Select(v => (JsonNode)JsonValue.Create(v))]),
        ["basePointSource"] = basePointSource, ["units"] = "mm", ["scaleRule"] = scaleRule,
    };

    private static JsonObject Asset(
        string id, string category, string name, string? description, JsonObject source, string format, string disposition,
        JsonObject? insertion = null, JsonArray? parameters = null, string? layer = null, JsonArray? tags = null,
        JsonArray? drawingTypes = null, JsonArray? loadedBy = null, JsonObject? props = null) => new()
    {
        ["id"] = id,
        ["category"] = category,
        ["name"] = name,
        ["description"] = description,
        ["source"] = source,
        ["format"] = format,
        ["disposition"] = disposition,
        ["insertion"] = insertion,
        ["parameters"] = parameters ?? [],
        ["layer"] = layer,
        ["tags"] = tags ?? [],
        ["drawingTypes"] = drawingTypes ?? [],
        ["loadedBy"] = loadedBy ?? [],
        ["props"] = props ?? [],
    };

    private static string Snake(string s) => JsonNamingPolicy.SnakeCaseLower.ConvertName(s);

    private static double R(double v) => Math.Round(v, 6);

    // ------------------------------------------------------------------ legacy files (assets/manifest.json)

    private sealed record LegacyFile(string RelPath, long Size, string? Sha256, string Disposition, string Reason, string Target);

    private static string CategoryOf(string target) => target switch
    {
        "blocks" => "block",
        "sections" => "section-table",
        "connections" => "connection-table",
        "project-defaults" => "project-defaults",
        "templates/new-project" => "project-template",
        "linetypes" => "linetype",
        "fonts" => "font-map",
        "command-aliases" => "command-alias",
        "dialogs" => "dialog",
        "palette" => "tool-palette",
        "quantities" => "quantity-workbook",
        "slides" => "slide",
        "icons" => "icon-set",
        "commands" or "fas-catalog" => "command-catalog",
        "plot-spec" => "plot-spec",
        "docs" => "reference-doc",
        _ when target.StartsWith("golden/", StringComparison.Ordinal) => "golden-reference",
        _ => throw new InvalidOperationException($"assets/manifest.json target '{target}' has no registry category; add it to AssetRegistryBuilder.CategoryOf."),
    };

    private static string Ext(string rel)
    {
        var e = Path.GetExtension(rel);
        return e.Length > 1 ? e[1..].ToLowerInvariant() : "none";
    }

    private static string Stem(string rel) => Path.GetFileNameWithoutExtension(rel.Replace('\\', '/').Split('/')[^1]);

    private static string Folder(string rel)
    {
        var i = rel.LastIndexOf('/');
        return i < 0 ? "" : rel[..i];
    }

    private static string StripRoot(string rel) => rel.StartsWith("HSSTEEL/", StringComparison.Ordinal) ? rel["HSSTEEL/".Length..] : rel;

    private static IEnumerable<JsonObject> LegacyAssets(JsonElement manifest)
    {
        var files = manifest.GetProperty("files").EnumerateArray()
            .Select(f => new LegacyFile(
                f.GetProperty("relPath").GetString()!, f.GetProperty("size").GetInt64(),
                f.TryGetProperty("sha256", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null,
                f.GetProperty("disposition").GetString()!, f.GetProperty("reason").GetString() ?? "", f.GetProperty("target").GetString() ?? "-"))
            .Where(f => f.Disposition != "excluded")
            .ToList();

        // Icons: one asset per folder.
        foreach (var g in files.Where(f => CategoryOf(f.Target) == "icon-set").GroupBy(f => Folder(f.RelPath), StringComparer.Ordinal))
        {
            var members = g.OrderBy(f => f.RelPath, StringComparer.Ordinal).ToList();
            var formats = string.Join("+", members.Select(m => Ext(m.RelPath)).Distinct().Order(StringComparer.Ordinal));
            yield return Asset(
                $"icon-set/{StripRoot(g.Key)}", "icon-set", StripRoot(g.Key), members[0].Reason,
                Source("legacy", g.Key, size: members.Sum(m => m.Size)), formats, members[0].Disposition,
                tags: Arr("icon", "ui"),
                loadedBy: new JsonArray(Use("csharp", "HsSteel.Knowledge.Ingest.IconCodec"), Use("knowledge-db", "icon")),
                props: new JsonObject
                {
                    ["manifestTarget"] = members[0].Target,
                    ["count"] = members.Count,
                    ["members"] = Arr([.. members.Select(m => m.RelPath[(g.Key.Length + 1)..])]),
                });
        }

        var singles = files.Where(f => CategoryOf(f.Target) != "icon-set").ToList();
        var stemCount = singles.GroupBy(f => (CategoryOf(f.Target), Stem(f.RelPath).ToLowerInvariant())).ToDictionary(g => g.Key, g => g.Count());
        foreach (var f in singles)
        {
            var category = CategoryOf(f.Target);
            var stem = Stem(f.RelPath);
            // Stem when unique in the category, else the relative path (with extension) without the leading HSSTEEL/.
            var slug = stemCount[(category, stem.ToLowerInvariant())] == 1 ? stem : StripRoot(f.RelPath);
            var props = new JsonObject { ["manifestTarget"] = f.Target, ["manifestReason"] = f.Reason };
            var a = Asset($"{category}/{slug}", category, Path.GetFileName(f.RelPath), f.Reason,
                Source("legacy", f.RelPath, sha256: f.Sha256, size: f.Size), Ext(f.RelPath), f.Disposition, props: props);
            Enrich(a, category, f, stem);
            yield return a;
        }
    }

    /// <summary>Category-specific parameters/usage for a legacy file (only what the code base actually does with it).</summary>
    private static void Enrich(JsonObject a, string category, LegacyFile f, string stem)
    {
        var props = a["props"]!.AsObject();
        switch (category)
        {
            case "block":
            {
                var cat = BlockCategory(stem);
                props["blockCategory"] = cat;
                props["blockCategorySource"] = "filename-rule";
                props["blockName"] = stem;
                props["metadataAtRuntime"] = "base point, extents, attribute tags and layers: hs_asset_get kind=block key=<blockName> (hs_assets.db)";
                a["insertion"] = Insertion(null, "unknown", cat is "frame" ? "sheet-scale" : null);
                a["parameters"] = new JsonArray(
                    Param("position", "point2d", "mm", required: true, description: "Insertion point in model space"),
                    Param("rotation", "number", "deg", 0),
                    Param("scale", "number", "ratio", 1, description: cat is "frame" ? "Frames are inserted × sheet scale (HS-STEEL practice: frame × SCL)" : null),
                    Param("attributes", "object", description: "{TAG: value}; tags are checked against the block definition by hs_block_insert_plan"));
                a["tags"] = Arr(["block", cat]);
                var loaded = new JsonArray(
                    Use("csharp", "HsSteel.Assets.Blocks.BlockCatalog"),
                    Use("knowledge-db", "block", "with block_attribute, block_layer"),
                    Use("mcp-tool", "hs_asset_get", "kind=block"),
                    Use("mcp-tool", "hs_block_insert_plan"),
                    Use("power-cad", "cad_import_block", "import {name, path=<legacy root>/<source.path>}"),
                    Use("power-cad", "cad_create", "type=insert"));
                if (cat is "frame")
                {
                    loaded.Add(Use("mcp-tool", "hs_frame_set", "path=<legacy root>/<source.path> to use it as the sheet frame"));
                }

                a["loadedBy"] = loaded;
                break;
            }

            case "section-table":
                props["family"] = stem;
                a["parameters"] = new JsonArray(Param("spec", "string", required: true, description: "Section designation within the family, e.g. H400x200x8x13"));
                a["tags"] = Arr("section", stem);
                a["drawingTypes"] = Arr("any");
                a["loadedBy"] = new JsonArray(
                    Use("csharp", "HsSteel.Assets.SectionTable"), Use("csharp", "HsSteel.Domain.SectionCatalog"),
                    Use("knowledge-db", "section"), Use("mcp-tool", "hs_section_search"), Use("mcp-tool", "hs_section_catalog_handoff", $"family={stem}"),
                    Use("mcp-resource", $"hs://sections/{stem}"));
                break;

            case "connection-table":
            {
                var m = ScssName().Match(stem);
                if (m.Success)
                {
                    props["splice"] = m.Groups[1].Value == "C" ? "column" : "girder";
                    props["boltSize"] = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                }

                a["parameters"] = new JsonArray(Param("section", "string", required: true, description: "H section, e.g. H400x200x8x13"));
                a["tags"] = Arr("connection", "splice", "bolt", "SCSS");
                a["drawingTypes"] = Arr("any");
                a["loadedBy"] = new JsonArray(
                    Use("csharp", "HsSteel.Domain.SpliceStandards"), Use("knowledge-db", "bolt"),
                    Use("mcp-tool", "hs_splice_standard", m.Success ? $"column={(m.Groups[1].Value == "C" ? "true" : "false")}, boltSize={m.Groups[2].Value}" : null),
                    Use("mcp-tool", "hs_connection_add", "kind=splice"));
                break;
            }

            case "project-defaults":
                a["tags"] = Arr("settings", "rules");
                a["drawingTypes"] = Arr("any");
                a["loadedBy"] = new JsonArray(Use("csharp", "HsSteel.Assets.ProjectSettings"), Use("csharp", "HsSteel.Domain.DetailRules.From"), Use("knowledge-db", "project_default"));
                break;

            case "project-template":
            {
                var ext = Ext(f.RelPath);
                a["tags"] = Arr("template", "new-project");
                var loaded = new JsonArray();
                if (ext == "dat")
                {
                    loaded.Add(Use("csharp", "HsSteel.Assets.Templates.DatFiles"));
                    loaded.Add(Use("knowledge-db", "template_dat_row"));
                    if (stem is "Numbering" or "Project")
                    {
                        loaded.Add(Use("csharp", "HsSteel.Mcp.Workspace", $"{stem}.dat in the attributes folder → DetailRules"));
                        a["drawingTypes"] = Arr("any");
                    }
                }
                else if (ext == "dwg")
                {
                    loaded.Add(Use("csharp", "HsSteel.Assets.Templates.DwgStyleReader"));
                    loaded.Add(Use("knowledge-db", "drafting_style"));
                }
                else if (ext is "xlsm" or "xlsx")
                {
                    loaded.Add(Use("csharp", "HsSteel.Assets.Templates.XlsxStructure"));
                    loaded.Add(Use("knowledge-db", "workbook"));
                }

                a["loadedBy"] = loaded;
                break;
            }

            case "linetype":
                a["tags"] = Arr("linetype", "style");
                a["loadedBy"] = new JsonArray(Use("csharp", "HsSteel.Assets.Support.SupportCatalog"), Use("knowledge-db", Ext(f.RelPath) == "mln" ? "mline_style" : "linetype"));
                break;
            case "font-map":
                a["tags"] = Arr("font", "style");
                a["loadedBy"] = new JsonArray(Use("csharp", "HsSteel.Assets.Support.SupportCatalog"), Use("knowledge-db", "font_map"));
                break;
            case "command-alias":
                a["tags"] = Arr("command");
                a["loadedBy"] = new JsonArray(Use("csharp", "HsSteel.Assets.Support.SupportCatalog"), Use("knowledge-db", "command_alias"));
                break;
            case "tool-palette":
                a["tags"] = Arr("palette", "ui");
                a["loadedBy"] = new JsonArray(Use("csharp", "HsSteel.Assets.Support.SupportCatalog"), Use("knowledge-db", "palette_item"));
                break;
            case "dialog":
                a["tags"] = Arr("dialog", "ui");
                a["loadedBy"] = Ext(f.RelPath) == "dcl" ? new JsonArray(Use("csharp", "HsSteel.Assets.Dialogs.DclParser"), Use("knowledge-db", "dialog")) : [];
                break;
            case "quantity-workbook":
                a["tags"] = Arr("bom", "quantity", "estimate");
                a["loadedBy"] = new JsonArray(Use("csharp", "HsSteel.Assets.Templates.XlsxStructure"), Use("knowledge-db", "workbook"));
                break;
            case "slide":
                a["tags"] = Arr("slide", "preview");
                a["loadedBy"] = new JsonArray(Use("csharp", "HsSteel.Assets.Slides.SlideReader", "SLD → SVG"), Use("knowledge-db", "slide"));
                break;
            case "command-catalog":
                a["tags"] = Arr("command", "reference");
                break;
            case "plot-spec":
                a["tags"] = Arr("plot", "reference");
                break;
            case "reference-doc":
                a["tags"] = Arr("doc", "reference");
                a["loadedBy"] = new JsonArray(Use("knowledge-db", "doc_chunk", "via tools/docs_chunker where supported"));
                break;
            case "golden-reference":
                props["projectSet"] = f.Target["golden/".Length..];
                a["tags"] = Arr("golden", "reference");
                break;
        }
    }

    [GeneratedRegex(@"^SCSS-([CG])(\d+)$")]
    private static partial Regex ScssName();

    /// <summary>
    /// First-pass block classification from the DWG file name only (the DWG itself is not in git). Unknown names are
    /// "misc"; a human review can refine the rule list. Order matters (first match wins).
    /// </summary>
    public static string BlockCategory(string name)
    {
        var n = name.ToUpperInvariant();
        bool Has(params string[] parts) => parts.Any(p => n.Contains(p, StringComparison.Ordinal));
        if (Has("REV_TITLE", "RIV_TITLE"))
        {
            return "revision";
        }

        if (Has("표지"))
        {
            return "cover";
        }

        if (Has("TABLE", "BILL_OF_MATERIALS", "_BOM", "MD_LIST", "수량", "주문서"))
        {
            return "table";
        }

        if (Has("NOTE", "일반사항", "질의서"))
        {
            return "note";
        }

        if (Regex.IsMatch(n, @"^A[34]-") || Has("TITLE"))
        {
            return "frame";
        }

        if (Has("WELD", "스켈럽"))
        {
            return "weld";
        }

        if (Has("단면", "HOLESECT"))
        {
            return "section_mark";
        }

        if (Regex.IsMatch(n, @"^MK") || Has("ORIENTATIONMARK", "조립방향", "UCS-XYZ"))
        {
            return "mark";
        }

        if (Regex.IsMatch(n, @"^HAS M"))
        {
            return "anchor";
        }

        if (Has("LUG"))
        {
            return "lug";
        }

        if (Has("LADDER"))
        {
            return "ladder";
        }

        if (Regex.IsMatch(n, @"^DECK-"))
        {
            return "deck";
        }

        return "misc";
    }

    // ------------------------------------------------------------------ code-defined assets

    private const string LayersCs = "src/HsSteel.Drafting/Layers.cs";
    private const string TextStylesCs = "src/HsSteel.Drafting/TextStyles.cs";
    private const string SheetFrameCs = "src/HsSteel.Drafting/SheetFrame.cs";
    private const string DetailGeneratorsCs = "src/HsSteel.Drafting/DetailGenerators.cs";
    private const string SheetsCs = "src/HsSteel.Drafting/Sheets.cs";
    private const string CalloutsCs = "src/HsSteel.Drafting/Callouts.cs";
    private const string AnnotationCs = "src/HsSteel.Drafting/Annotation.cs";
    private const string FabricationCs = "src/HsSteel.Domain/Fabrication.cs";
    private const string ModelCs = "src/HsSteel.Domain/Model.cs";
    private const string StandardOptionsCs = "src/HsSteel.Domain/StandardOptions.cs";
    private const string ProfileCs = "src/HsSteel.Domain/Profile.cs";
    private const string ProjectCs = "src/HsSteel.Modeling/Project.cs";
    private const string ProjectTemplatesCs = "src/HsSteel.Modeling/ProjectTemplates.cs";

    private static readonly string[] DrawingTool = ["hs_drawings_generate", "hs_drawings_to_powercad", "hs_draw_plan_handoff"];

    private static IEnumerable<JsonObject> CodeAssets(string repoRoot)
    {
        // Layers
        var layerConst = typeof(Layers).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => (string)f.GetRawConstantValue()!, f => f.Name, StringComparer.Ordinal);
        foreach (var (name, color, linetype) in Layers.All)
        {
            yield return Asset($"layer/{name}", "layer", name, null,
                Source("repo", LayersCs, $"HsSteel.Drafting.Layers.{layerConst[name]}"), "csharp", "code",
                layer: name, tags: Arr("layer", "style"), drawingTypes: Arr("any"),
                loadedBy: new JsonArray(Use("csharp", "HsSteel.Drafting.Layers.All"), Use("mcp-tool", "hs_drawings_to_powercad", "payload layers[] (created when missing)"), Use("power-cad", "cad_create_many", "layers")),
                props: new JsonObject { ["color"] = color, ["colorSystem"] = "ACI", ["linetype"] = linetype });
        }

        // Text / dim styles
        foreach (var def in TextStyles.TextStyleDefs().Select(n => n!.AsObject()))
        {
            var name = def["name"]!.GetValue<string>();
            yield return Asset($"text-style/{name}", "text-style", name, "Korean text style: txt.shx + big font whgtxt.shx (same pair as the legacy template's Standard/GHS styles)",
                Source("repo", TextStylesCs, "HsSteel.Drafting.TextStyles.TextStyleDefs"), "csharp", "code",
                tags: Arr("text", "style", "korean"), drawingTypes: Arr("any"),
                loadedBy: new JsonArray(Use("mcp-tool", "hs_drawings_to_powercad", "payload text_styles[] (created when missing; existing styles never changed)"), Use("power-cad", "cad_create_many", "text_styles")),
                props: def.DeepClone().AsObject());
        }

        foreach (var def in TextStyles.DimStyleDefs().Select(n => n!.AsObject()))
        {
            var name = def["name"]!.GetValue<string>();
            var p = def.DeepClone().AsObject();
            p["templateDimTextHeight"] = DetailRules.TemplateDimText;
            yield return Asset($"dim-style/{name}", "dim-style", name, "Dimension style based on Standard with text style HS-KOR; template dimension text height on paper is DetailRules.TemplateDimText",
                Source("repo", TextStylesCs, "HsSteel.Drafting.TextStyles.DimStyleDefs"), "csharp", "code",
                layer: Layers.Dim, tags: Arr("dimension", "style"), drawingTypes: Arr("any"),
                loadedBy: new JsonArray(Use("mcp-tool", "hs_drawings_to_powercad", "payload dim_styles[]"), Use("power-cad", "cad_create_many", "dim_styles")),
                props: p);
        }

        // Title block / frame
        var fr = SheetFrame.A3Default;
        var slots = new JsonObject();
        var frameParams = new JsonArray(
            Param("position", "point2d", "mm", required: true, description: "Sheet origin in model space"),
            Param("sheet_scale", "number", "ratio", 1, true, description: "Drawing scale denominator; the frame is modelled 1:1 and inserted × sheet scale"));
        foreach (var (field, slot) in fr.TextSlots.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            slots[field] = new JsonObject { ["x"] = slot.X, ["y"] = slot.Y, ["height"] = slot.Height, ["justify"] = slot.Justify };
            frameParams.Add(Param(field, "string", description: "Title-block field (block attribute when the frame has it, else text at the slot)"));
        }

        yield return Asset($"title-block/{fr.BlockName}", "title-block", fr.BlockName,
            "Placeholder A3 frame (420x297, 10 mm border, 70 mm title strip). Replace with the company frame block via hs_frame_set.",
            Source("repo", SheetFrameCs, "HsSteel.Drafting.SheetFrame.A3Default"), "csharp", "code",
            insertion: Insertion([fr.BaseX, fr.BaseY, 0], "code", "sheet-scale"),
            parameters: frameParams, layer: Layers.Frame, tags: Arr("frame", "title-block", "도곽", "A3"), drawingTypes: Arr("any"),
            loadedBy: new JsonArray(Use("csharp", "HsSteel.Drafting.SheetComposer"), Use("mcp-tool", "hs_frame_set", "replaces this frame for all drawings"), Use("mcp-tool", "hs_drawings_to_powercad", "listed in blocks_needed")),
            props: new JsonObject
            {
                ["width"] = fr.Width, ["height"] = fr.Height,
                ["area"] = new JsonObject { ["x"] = fr.AreaX, ["y"] = fr.AreaY, ["w"] = fr.AreaW, ["h"] = fr.AreaH },
                ["textSlots"] = slots,
                ["placeholder"] = true,
            });

        // Drawing generators
        (string Id, string Symbol, string File, string Type, string? Prefix, string? Title, string Desc)[] gens =
        [
            ("part-detail", "HsSteel.Drafting.PartDetail", DetailGeneratorsCs, "part", "S", "SHAPE DETAIL", "Single-part shop detail (형강 단품도) for any profile"),
            ("plate-detail", "HsSteel.Drafting.PlateDetail", DetailGeneratorsCs, "plate", "P", "PLATE DETAIL", "Plate part detail (판재 상세)"),
            ("assembly-detail", "HsSteel.Drafting.AssemblyDetail", DetailGeneratorsCs, "assembly", "A", "ASSY DWG", "Assembly drawing (조립도)"),
            ("layout-plan", "HsSteel.Drafting.LayoutPlan", SheetsCs, "plan", "E", "ERECTION PLAN", "Erection plan (배치도) per level"),
            ("layout-elevation", "HsSteel.Drafting.LayoutElevation", SheetsCs, "elevation", "V", "ERECTION ELEVATION", "Erection elevations per grid (generated under DrawingSet.Kinds.Plan)"),
            ("bom-sheets", "HsSteel.Drafting.BomSheets", SheetsCs, "bom", "M", "BOM / 물량표", "Bill of materials sheets (assemblies, materials, bolts)"),
        ];
        foreach (var g in gens)
        {
            yield return Asset($"drawing-generator/{g.Id}", "drawing-generator", g.Symbol.Split('.')[^1], g.Desc,
                Source("repo", g.File, g.Symbol), "csharp", "code",
                tags: Arr("generator", g.Type), drawingTypes: Arr(g.Type),
                loadedBy: new JsonArray([Use("csharp", "HsSteel.Drafting.DrawingSet.Generate"), .. DrawingTool.Select(t => Use("mcp-tool", t))]),
                props: new JsonObject
                {
                    ["kindFlag"] = g.Type == "elevation" ? nameof(DrawingSet.Kinds.Plan) : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(g.Type),
                    ["sheetPrefix"] = g.Prefix, ["sheetTitle"] = g.Title,
                });
        }

        var kinds = Enum.GetNames<DrawingSet.Kinds>().Where(k => k != nameof(DrawingSet.Kinds.All)).Select(k => k.ToLowerInvariant()).ToArray();
        yield return Asset("drawing-generator/drawing-set", "drawing-generator", "DrawingSet",
            "Generates the whole shop-drawing set of a project into the frame (E, V, A, S, P, M sheets)",
            Source("repo", SheetsCs, "HsSteel.Drafting.DrawingSet.Generate"), "csharp", "code",
            parameters: new JsonArray(
                Param("project", "string", required: true, description: "Project name in the workspace"),
                Param("kinds", "enum", values: kinds, description: "Subset of sheet kinds (array); default all"),
                Param("sheet", "string", description: "Only this sheet number, e.g. A-001 (hs_drawings_to_powercad)")),
            tags: Arr("generator", "sheet-set"), drawingTypes: Arr("any"),
            loadedBy: new JsonArray([.. DrawingTool.Select(t => Use("mcp-tool", t)), Use("mcp-tool", "hs_draw_plan")]),
            props: new JsonObject { ["kinds"] = Arr(kinds) });
        yield return Asset("drawing-generator/sheet-composer", "drawing-generator", "SheetComposer",
            "Places generated views into frames, numbers sheets (<prefix>-NNN), writes title-block fields",
            Source("repo", SheetsCs, "HsSteel.Drafting.SheetComposer"), "csharp", "code",
            layer: Layers.Frame, tags: Arr("sheet", "frame"), drawingTypes: Arr("any"),
            loadedBy: new JsonArray(Use("csharp", "HsSteel.Drafting.DrawingSet.Generate")));

        // Annotation standards
        yield return Asset("annotation-standard/callouts", "annotation-standard", "Callouts",
            "Bolt notes, weld symbols, member marks (balloons) and the enlarged section view",
            Source("repo", CalloutsCs, "HsSteel.Drafting.Callouts"), "csharp", "code",
            tags: Arr("annotation", "bolt", "weld", "mark"), drawingTypes: Arr("assembly", "part", "plate"),
            loadedBy: new JsonArray(Use("csharp", "HsSteel.Drafting.AssemblyDetail"), Use("csharp", "HsSteel.Drafting.PartDetail")),
            props: new JsonObject { ["minLeaderPaper"] = Callouts.MinLeaderPaper, ["boltNoteDefaultType"] = "HTB" });
        foreach (var (kind, block) in new[] { (WeldKind.Fillet, Callouts.FilletBlock), (WeldKind.Groove, Callouts.GrooveBlock) })
        {
            yield return Asset($"annotation-standard/{block}", "annotation-standard", block,
                $"{kind} weld symbol block name used by the generators (placeholder a backend may swap for the legacy HS-STEEL blocks)",
                Source("repo", CalloutsCs, $"HsSteel.Drafting.Callouts.{kind}Block"), "csharp", "code",
                insertion: Insertion(null, "unknown", "paper"),
                parameters: new JsonArray(Param("size", "number", "mm", required: true, description: "Weld leg size"), Param("mark", "string")),
                layer: Layers.Weld, tags: Arr("weld", "symbol", kind.ToString().ToLowerInvariant()), drawingTypes: Arr("assembly"),
                loadedBy: new JsonArray(Use("csharp", "HsSteel.Drafting.Callouts.Weld")),
                props: new JsonObject { ["weldKind"] = kind.ToString(), ["placeholder"] = true });
        }

        yield return Asset("annotation-standard/annotate", "annotation-standard", "Annotate",
            "Chain dimensions, marks, grid bubbles and tables",
            Source("repo", AnnotationCs, "HsSteel.Drafting.Annotate"), "csharp", "code",
            layer: Layers.Dim, tags: Arr("annotation", "dimension", "table"), drawingTypes: Arr("any"),
            loadedBy: new JsonArray(Use("csharp", "HsSteel.Drafting.DrawingSet.Generate")));

        // Data tables
        yield return Asset("data-table/standard-scales", "data-table", "SheetFrame.StandardScales", "Standard drawing scales (denominators) used to fit views",
            Source("repo", SheetFrameCs, "HsSteel.Drafting.SheetFrame.StandardScales"), "csharp", "code",
            tags: Arr("scale"), drawingTypes: Arr("any"),
            props: new JsonObject { ["values"] = new JsonArray([.. SheetFrame.StandardScales.Select(v => (JsonNode)v)]) });
        yield return Asset("data-table/paper-constants", "data-table", "Paper", "Paper-size constants of the detail generators (mm on paper)",
            Source("repo", DetailGeneratorsCs, "HsSteel.Drafting.Paper"), "csharp", "code",
            tags: Arr("paper", "text"), drawingTypes: Arr("assembly", "part", "plate"),
            props: new JsonObject { ["sectionMax"] = Paper.SectionMax, ["lengthBudget"] = Paper.LengthBudget, ["text"] = Paper.Text, ["dimRow"] = Paper.DimRow });
        yield return Asset("data-table/weld-min-fillet", "data-table", "WeldRules.MinFillet",
            "Minimum fillet weld leg by the thicker part (KDS 14 31 25 / AWS D1.1); leg capped by the thinner part",
            Source("repo", FabricationCs, "HsSteel.Domain.WeldRules.MinFillet"), "csharp", "code",
            tags: Arr("weld", "fillet"), drawingTypes: Arr("assembly", "part", "plate"),
            props: new JsonObject
            {
                ["unit"] = "mm",
                ["rows"] = new JsonArray([.. WeldRules.MinFillet.Select(r => (JsonNode)new JsonObject { ["upTo"] = r.UpTo >= double.MaxValue / 2 ? null : r.UpTo, ["minLeg"] = r.MinLeg })]),
                ["note"] = "upTo null = no upper bound",
            });
        yield return Asset("data-table/bolt-gauge-angle", "data-table", "BoltGauges.Angle", "Angle leg → bolt gauge lines g1/g2 and max bolt (AIJ / KS)",
            Source("repo", FabricationCs, "HsSteel.Domain.BoltGauges.Angle"), "csharp", "code",
            tags: Arr("bolt", "gauge", "ANGLE"), drawingTypes: Arr("assembly", "part"),
            props: new JsonObject
            {
                ["unit"] = "mm",
                ["rows"] = new JsonArray([.. BoltGauges.Angle.Select(r => (JsonNode)new JsonObject { ["leg"] = r.Leg, ["g1"] = r.G1, ["g2"] = r.G2, ["maxBolt"] = r.MaxBolt })]),
            });
        yield return Asset("data-table/bolt-gauge-flange", "data-table", "BoltGauges.Flange", "H/T flange width → gauge between the two flange bolt lines (AIJ g1)",
            Source("repo", FabricationCs, "HsSteel.Domain.BoltGauges.Flange"), "csharp", "code",
            tags: Arr("bolt", "gauge", "H-BEAM"), drawingTypes: Arr("assembly", "part"),
            props: new JsonObject
            {
                ["unit"] = "mm",
                ["rows"] = new JsonArray([.. BoltGauges.Flange.Select(r => (JsonNode)new JsonObject { ["width"] = r.Width, ["gauge"] = r.Gauge })]),
            });
        yield return Asset("data-table/bolt-grades", "data-table", "Bolts", "Bolt grades and the standard hole rule",
            Source("repo", FabricationCs, "HsSteel.Domain.Bolts"), "csharp", "code",
            tags: Arr("bolt", "grade", "hole"), drawingTypes: Arr("assembly", "part", "plate", "bom"),
            props: new JsonObject
            {
                ["grades"] = Arr(Bolts.F10T, Bolts.S10T, Bolts.MBolt),
                ["holeExamples"] = new JsonObject { ["M16"] = Bolts.HoleFor(16), ["M20"] = Bolts.HoleFor(20), ["M22"] = Bolts.HoleFor(22), ["M24"] = Bolts.HoleFor(24) },
                ["holeRule"] = "standard hole = bolt + 2 up to M22, bolt + 3 from M24",
            });
        var heads = new JsonObject();
        foreach (var t in Enum.GetValues<AssemblyType>())
        {
            heads[Snake(t.ToString())] = new JsonObject
            {
                ["prefix"] = AssemblyTypes.Prefix(t),
                ["altPrefix"] = StandardOptions.AltPrefix(t),
                ["numberingKey"] = AssemblyTypes.NumberingKeys.TryGetValue(t, out var k) ? k : null,
            };
        }

        yield return Asset("data-table/assembly-types", "data-table", "AssemblyTypes", "Assembly types and mark heads (legacy Numbering.dat defaults; alt scheme; Numbering.dat override keys)",
            Source("repo", ModelCs, "HsSteel.Domain.AssemblyTypes"), "csharp", "code",
            tags: Arr("mark", "numbering", "assembly"), drawingTypes: Arr("any"),
            loadedBy: new JsonArray(Use("mcp-tool", "hs_member_add", "type")),
            props: new JsonObject { ["types"] = heads, ["defaultMarkDigits"] = StandardOptions.DefaultMarkDigits });

        // Standard options / detail rules
        yield return Asset("standard-option/detail-rules", "standard-option", "DetailRules",
            "Fabrication detail rules of a project (Project.dat keys SCALLOP/ENDGAGE/SHOLE/WDGAP/SWS, Numbering.dat heads); JSON names as in the project file",
            Source("repo", ModelCs, "HsSteel.Domain.DetailRules"), "csharp", "code",
            parameters: RecordParams(typeof(DetailRules)), tags: Arr("rules", "settings"), drawingTypes: Arr("any"),
            loadedBy: new JsonArray(Use("mcp-tool", "hs_project_rules"), Use("csharp", "HsSteel.Domain.DetailRules.From", "from legacy Project.dat")));
        yield return Asset("standard-option/project-options", "standard-option", "StandardOptions",
            "Project standard options stored in the project JSON (bolt length table, hole rule, mark scheme/format/digits)",
            Source("repo", StandardOptionsCs, "HsSteel.Domain.StandardOptions"), "csharp", "code",
            parameters: new JsonArray(
                Param("bolt_length_table", "enum", null, StandardOptions.DefaultBoltLengthTable, values: StandardOptions.BoltLengthTables),
                Param("hole_rule", "enum", null, StandardOptions.DefaultHoleRule, values: StandardOptions.HoleRules),
                Param("mark_scheme", "enum", null, StandardOptions.DefaultMarkScheme, values: StandardOptions.MarkSchemes),
                Param("mark_format", "enum", null, StandardOptions.DefaultMarkFormat, values: StandardOptions.MarkFormats),
                Param("mark_digits", "integer", null, StandardOptions.DefaultMarkDigits, description: $"1..{StandardOptions.MaxMarkDigits}")),
            tags: Arr("options", "bolt", "hole", "mark"), drawingTypes: Arr("any"),
            loadedBy: new JsonArray(Use("mcp-tool", "hs_project_options", "camelCase names boltLengthTable/holeRule/markScheme/markFormat/markDigits")));

        // Rules catalog
        var rulesPath = Path.Combine(repoRoot, RulesRel);
        using (var rules = JsonDocument.Parse(File.ReadAllBytes(rulesPath)))
        {
            var cats = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var count = 0;
            foreach (var r in rules.RootElement.GetProperty("rules").EnumerateArray())
            {
                count++;
                var c = r.TryGetProperty("category", out var cv) ? cv.GetString() ?? "" : "";
                cats[c] = cats.GetValueOrDefault(c) + 1;
            }

            yield return Asset("rules-catalog/rules", "rules-catalog", "RULES_CATALOG",
                "Evidence-backed detailing rules (trust graded) used by hs_rules_search / hs_explain / hs_graph_rag",
                Source("repo", RulesRel, "HsSteel.Knowledge.Rules.RulesCatalog"), "json", "code",
                tags: Arr("rules"), drawingTypes: Arr("any"),
                loadedBy: new JsonArray(Use("mcp-tool", "hs_rules_search"), Use("mcp-tool", "hs_explain"), Use("mcp-tool", "hs_graph_rag")),
                props: new JsonObject
                {
                    ["schema"] = rules.RootElement.TryGetProperty("schema", out var s) ? s.GetString() : null,
                    ["count"] = count,
                    ["byCategory"] = new JsonObject([.. cats.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))]),
                    ["doc"] = "docs/RULES_CATALOG.md",
                });
        }

        // Profile families (ShapeKind) with the doc comment text from Profile.cs
        var docs = ShapeDocs(File.ReadAllText(Path.Combine(repoRoot, ProfileCs)));
        foreach (var k in Enum.GetValues<ShapeKind>())
        {
            yield return Asset($"profile-family/{k}", "profile-family", k.ToString(), docs.GetValueOrDefault(k.ToString()),
                Source("repo", ProfileCs, $"HsSteel.Domain.ShapeKind.{k}"), "csharp", "code",
                tags: Arr("section", "profile"), drawingTypes: Arr("any"),
                loadedBy: new JsonArray(Use("csharp", "HsSteel.Domain.Profile"), Use("csharp", "HsSteel.Drafting.MemberViews")));
        }

        // Connection kinds (JsonDerivedType on ConnectionDef)
        foreach (var d in typeof(ConnectionDef).GetCustomAttributes<JsonDerivedTypeAttribute>().OrderBy(d => (string)d.TypeDiscriminator!, StringComparer.Ordinal))
        {
            var disc = (string)d.TypeDiscriminator!;
            var p = new JsonObject { ["discriminator"] = disc, ["type"] = d.DerivedType.FullName };
            if (disc == "splice")
            {
                p["standardTables"] = "connection-table/SCSS-*";
            }

            yield return Asset($"connection-detail/{disc}", "connection-detail", disc, $"{d.DerivedType.Name}: project connection kind \"{disc}\"",
                Source("repo", ProjectCs, d.DerivedType.FullName), "csharp", "code",
                parameters: RecordParams(d.DerivedType), tags: Arr("connection", disc), drawingTypes: Arr("assembly", "part", "plate", "bom"),
                loadedBy: new JsonArray(Use("mcp-tool", "hs_connection_add", $"{{\"kind\":\"{disc}\", ...}}"), Use("csharp", "HsSteel.Modeling.ModelBuilder")),
                props: p);
        }

        // Model templates
        yield return Asset("model-template/frame", "model-template", "ProjectTemplates.Frame",
            "Complete multi-storey frame from bay sizes: grids, levels, columns (base/caps/optional splices), girders (shear tab or end plate), secondary beams",
            Source("repo", ProjectTemplatesCs, "HsSteel.Modeling.ProjectTemplates.Frame"), "csharp", "code",
            parameters: RecordParams(typeof(FrameSpec)), tags: Arr("template", "frame", "model"), drawingTypes: Arr("any"),
            loadedBy: new JsonArray(Use("mcp-tool", "hs_project_frame")));
    }

    private static Dictionary<string, string> ShapeDocs(string source)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(source, @"///\s*<summary>(.*?)</summary>\s*\r?\n\s*(\w+),"))
        {
            d[m.Groups[2].Value] = m.Groups[1].Value.Trim().Replace("&lt;", "<").Replace("&gt;", ">");
        }

        return d;
    }

    /// <summary>Parameters of a record: primary-constructor parameters plus public init-only scalar properties (snake_case JSON names).</summary>
    private static JsonArray RecordParams(Type t)
    {
        var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var list = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ctor.GetParameters())
        {
            seen.Add(p.Name!);
            list.Add(ParamOf(p.Name!, p.ParameterType, p.HasDefaultValue ? p.DefaultValue : null, !p.HasDefaultValue));
        }

        object? sample = null;
        foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.MetadataToken))
        {
            var isInit = prop.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit") == true;
            var scalar = prop.PropertyType == typeof(string) || prop.PropertyType == typeof(int) || prop.PropertyType == typeof(double) || prop.PropertyType == typeof(bool);
            if (seen.Contains(prop.Name) || !isInit || !scalar)
            {
                continue;
            }

            sample ??= TryDefault(t, ctor);
            list.Add(ParamOf(prop.Name, prop.PropertyType, sample is null ? null : prop.GetValue(sample), false));
        }

        return list;
    }

    private static object? TryDefault(Type t, ConstructorInfo ctor)
    {
        var ps = ctor.GetParameters();
        return ps.All(p => p.HasDefaultValue) ? ctor.Invoke([.. ps.Select(p => p.DefaultValue)]) : null;
    }

    private static JsonObject ParamOf(string name, Type type, object? def, bool required)
    {
        var snake = Snake(name);
        if (type.IsEnum)
        {
            return Param(snake, "enum", null, def is null ? null : Snake(def.ToString()!), required, Enum.GetNames(type).Select(Snake));
        }

        var (kind, node) = type switch
        {
            _ when type == typeof(double) => ("number", def is double d ? JsonValue.Create(d) : null),
            _ when type == typeof(int) => ("integer", def is int i ? JsonValue.Create(i) : null),
            _ when type == typeof(bool) => ("boolean", def is bool b ? JsonValue.Create(b) : null),
            _ when type == typeof(string) => ("string", def is string s ? JsonValue.Create(s) : null),
            _ when type.IsAssignableTo(typeof(System.Collections.IEnumerable)) => ("array", (JsonNode?)null),
            _ => ("object", (JsonNode?)null),
        };
        string? unit = kind is "number" || (kind == "integer" && name.Contains("Size", StringComparison.Ordinal)) ? "mm" : null;
        if (kind == "array" && type.IsGenericType && type.GetGenericArguments()[0] == typeof(double))
        {
            unit = "mm";
        }

        return Param(snake, kind, unit, node, required);
    }
}
