using HsSteel.Assets;
using HsSteel.Domain;
using HsSteel.Drafting;
using HsSteel.Modeling;
using System.Text.Json;
using ModelContextProtocol;

namespace HsSteel.Mcp;

/// <summary>
/// Shared state of the server: section catalogue, splice standards and detailing rules from the
/// HS-STEEL assets, plus a folder of project JSON files.
/// Environment: HS_STEEL_ASSETS (folder with attributes/*.dat; default: bundled assets next to the exe,
/// then C:\HS-STEEL\HSSTEEL), HS_STEEL_WORKSPACE (project folder; default %USERPROFILE%\hs-steel-projects),
/// HS_STEEL_FRAME (company frame DWG/DXF), HS_STEEL_FRAME_BLOCK (block name inside it).
/// </summary>
public sealed class Workspace
{
    public required string AttributesDir { get; init; }

    public required string ProjectsDir { get; init; }

    public required SectionCatalog Catalog { get; init; }

    public required SpliceStandards Splices { get; init; }

    public required DetailRules Rules { get; init; }

    public SheetFrame Frame { get; set; } = SheetFrame.A3Default;

    public static Workspace FromEnvironment()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("HS_STEEL_ASSETS"),
            Path.Combine(AppContext.BaseDirectory, "assets"),
            @"C:\HS-STEEL\HSSTEEL",
        };
        var root = candidates.FirstOrDefault(c => c is not null && Directory.Exists(Path.Combine(c, "attributes"))) ?? AppContext.BaseDirectory;
        var projects = Environment.GetEnvironmentVariable("HS_STEEL_WORKSPACE")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "hs-steel-projects");
        var ws = Create(root, projects);
        var frame = Environment.GetEnvironmentVariable("HS_STEEL_FRAME");
        if (frame is not null && File.Exists(frame))
        {
            ws.Frame = FrameSource.FromFile(frame, Environment.GetEnvironmentVariable("HS_STEEL_FRAME_BLOCK"));
        }

        return ws;
    }

    public static Workspace Create(string assetsRoot, string projectsDir)
    {
        var attr = Path.Combine(assetsRoot, "attributes");
        var projectDat = Path.Combine(attr, "Project.dat");
        var numberingDat = Path.Combine(attr, "Numbering.dat");
        var rules = File.Exists(projectDat) ? DetailRules.From(ProjectSettings.Load(projectDat)) : new DetailRules();
        if (File.Exists(numberingDat))
        {
            // NUM-001: assembly mark heads from the legacy Numbering.dat (M83-*-HD-BOX) when the asset folder has one.
            var heads = AssemblyTypes.HeadsFrom(ProjectSettings.Load(numberingDat));
            rules = rules with { MarkHeads = heads.Count > 0 ? heads : rules.MarkHeads };
        }

        return new Workspace
        {
            AttributesDir = attr,
            ProjectsDir = projectsDir,
            Catalog = SectionCatalog.Load(attr),
            Splices = SpliceStandards.Load(attr),
            Rules = rules,
        };
    }

    public string PathOf(string project)
    {
        var safe = string.Concat(project.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(ProjectsDir, safe + ".hsproj.json");
    }

    public Project Load(string project)
    {
        var path = PathOf(project);
        if (!File.Exists(path))
        {
            // McpException: the MCP SDK only forwards McpException messages to the client; any other
            // exception becomes a generic "An error occurred invoking ..." without the hint.
            throw new McpException($"Project '{project}' does not exist. Create it with hs_project_new or hs_project_frame.");
        }

        return Parse(File.ReadAllText(path), $"Project file '{path}'");
    }

    /// <summary>Deserialises project JSON, turning schema errors into a client-visible <see cref="McpException"/>.</summary>
    public static Project Parse(string json, string what = "Project JSON")
    {
        try
        {
            return Project.FromJson(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new McpException($"{what} is not valid: {ex.Message}");
        }
    }

    public void Save(Project p)
    {
        Directory.CreateDirectory(ProjectsDir);
        File.WriteAllText(PathOf(p.Name), p.ToJson());
    }

    public ModelResult Build(Project p) => new ModelBuilder(Catalog, Splices).Build(Apply(p));

    public DrawingSet Drawings(Project p, DrawingSet.Kinds kinds) => DrawingSet.Generate(Apply(p), Catalog, Splices, Frame, kinds);

    private Project Apply(Project p)
    {
        p.Rules ??= Rules;
        return p;
    }
}
