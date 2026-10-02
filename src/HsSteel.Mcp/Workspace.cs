using HsSteel.Assets;
using HsSteel.Domain;
using HsSteel.Drafting;
using HsSteel.Modeling;

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
        return new Workspace
        {
            AttributesDir = attr,
            ProjectsDir = projectsDir,
            Catalog = SectionCatalog.Load(attr),
            Splices = SpliceStandards.Load(attr),
            Rules = File.Exists(projectDat) ? DetailRules.From(ProjectSettings.Load(projectDat)) : new DetailRules(),
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
            throw new FileNotFoundException($"Project '{project}' does not exist. Create it with hs_project_new or hs_project_frame.", path);
        }

        return Project.FromJson(File.ReadAllText(path));
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
