using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HsSteel.Knowledge;

/// <summary>One file of the legacy HS-STEEL tree. Disposition is ingest | reference | excluded.</summary>
public sealed record ManifestEntry(string RelPath, long Size, string Sha256, string Disposition, string Reason, string Target);

/// <summary>Deterministic file ledger of the legacy tree (rules from docs/ASSET_LEDGER.md).</summary>
public sealed record Manifest(string Schema, string RootName, int FileCount, IReadOnlyDictionary<string, int> Counts, IReadOnlyList<ManifestEntry> Files)
{
    public const string SchemaId = "hs-steel-manifest/1";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions).Replace("\r\n", "\n") + "\n";

    public static Manifest FromJson(string json) =>
        JsonSerializer.Deserialize<Manifest>(json, JsonOptions) ?? throw new InvalidDataException("empty manifest");

    public static Manifest Load(string path) => FromJson(File.ReadAllText(path));

    public void Save(string path) => File.WriteAllText(path, ToJson(), new System.Text.UTF8Encoding(false));
}

public static class ManifestScanner
{
    public const string Ingest = "ingest";
    public const string Reference = "reference";
    public const string Excluded = "excluded";
    public const string Unknown = "unknown";

    /// <summary>Legacy full root: parent of env HS_STEEL_LEGACY (default C:\HS-STEEL\HSSTEEL) -> C:\HS-STEEL.</summary>
    public static string DefaultRoot()
    {
        var legacy = Environment.GetEnvironmentVariable("HS_STEEL_LEGACY");
        if (!string.IsNullOrWhiteSpace(legacy) && Directory.Exists(legacy))
        {
            var parent = Directory.GetParent(Path.GetFullPath(legacy).TrimEnd('\\', '/'));
            if (parent != null)
            {
                return parent.FullName;
            }
        }

        return @"C:\HS-STEEL";
    }

    public static Manifest Scan(string root)
    {
        var full = Path.GetFullPath(root).TrimEnd('\\', '/');
        var files = Directory.EnumerateFiles(full, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = 0,
                IgnoreInaccessible = false,
            })
            .Select(p => Path.GetRelativePath(full, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var rows = new List<ManifestEntry>(files.Count);
        foreach (var rel in files)
        {
            var path = Path.Combine(full, rel);
            var (disp, reason, target) = Classify(rel);
            rows.Add(new ManifestEntry(rel, new FileInfo(path).Length, Sha256(path), disp, reason, target));
        }

        var counts = rows.GroupBy(r => r.Disposition).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());
        return new Manifest(Manifest.SchemaId, Path.GetFileName(full), rows.Count, counts, rows);
    }

    public static string Sha256(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }

    private static readonly Regex MemberData = new(@"^\.m\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Pure classification rule for a '/'-separated path relative to the HS-STEEL root.</summary>
    public static (string Disposition, string Reason, string Target) Classify(string relPath)
    {
        var p = relPath.Replace('\\', '/');
        var lower = p.ToLowerInvariant();
        var slash = lower.LastIndexOf('/');
        var name = lower[(slash + 1)..];
        var ext = Path.GetExtension(name);
        var dir = slash < 0 ? string.Empty : lower[..slash];

        // 1. dongle / licensing mechanism - never reconstructed
        if (lower.StartsWith("hsrockey/", StringComparison.Ordinal) || name.StartsWith("csrockey", StringComparison.Ordinal) || name.StartsWith("r4nd", StringComparison.Ordinal))
        {
            return (Excluded, "dongle licensing mechanism (not a reconstruction target)", "-");
        }

        // 2. previous analysis byproducts
        if (dir.Length == 0 || lower.StartsWith(".anchor/", StringComparison.Ordinal))
        {
            return (Excluded, "analysis byproduct (root HS_STEEL_OFFLINE_*, *.scr, .anchor)", "-");
        }

        if (name == "ida.py" || ext is ".id0" or ".id1" or ".nam" or ".til")
        {
            return (Excluded, "analysis byproduct (disassembler database/script)", "-");
        }

        // 3. backup / cache / log
        if (ext is ".bak" or ".err" or ".log" or ".cdc" or ".db" || name.Contains(".bak.", StringComparison.Ordinal))
        {
            return (Excluded, "backup/cache/log", "-");
        }

        // 4. member data (.Mxx) is always golden reference
        if (MemberData.IsMatch(ext))
        {
            return (Reference, "golden: project member data (.Mxx), never ingested", "golden/" + p[..slash]);
        }

        // 5. company project sets / work samples
        if (dir.StartsWith("hssteel/새공사-설치용", StringComparison.Ordinal))
        {
            return ext is ".dat" or ".dwg" or ".xlsm"
                ? (Ingest, "new-project default set", "templates/new-project")
                : (Reference, "new-project set installer/script/plot style", "templates/new-project");
        }

        if (dir.StartsWith("hssteel/새공사-", StringComparison.Ordinal) || dir.StartsWith("hssteel/작업", StringComparison.Ordinal))
        {
            return (Reference, "golden: company/sample project set, never ingested", "golden/" + p[..slash]);
        }

        // 6. section tables
        if (dir == "hssteel/attributes" && ext == ".dat")
        {
            if (name.StartsWith("scss-", StringComparison.Ordinal))
            {
                return (Ingest, "connection (splice) table", "connections");
            }

            return name == "project.dat"
                ? (Ingest, "project default settings", "project-defaults")
                : (Ingest, "section table", "sections");
        }

        if (dir == "attributes")
        {
            return (Reference, "older (2024) section tables, diff report only", "sections-diff");
        }

        // 7. plot assets (ledger v0.3: plotting is the user's responsibility) - reference only
        if (ext == ".ctb" || lower.StartsWith("allplot/", StringComparison.Ordinal))
        {
            return lower.StartsWith("allplot/allplot-setup/", StringComparison.Ordinal)
                ? (Reference, "golden: plot setup sample", "golden/allplot")
                : (Reference, "plot asset (v0.3: reference only)", "plot-spec");
        }

        // 8. blocks
        if (dir == "hssteel/block" && ext == ".dwg")
        {
            return (Ingest, "detail block library", "blocks");
        }

        // 9. support
        if (dir == "hssteel/support" || dir.StartsWith("hssteel/support/", StringComparison.Ordinal))
        {
            switch (ext)
            {
                case ".bmp" or ".png": return (Ingest, "ribbon/toolbar icon", "icons");
                case ".atc" or ".xtp": return (Ingest, "tool palette definition", "palette");
                case ".lin" or ".mln": return (Ingest, "linetype/multiline style", "linetypes");
                case ".fmp": return (Ingest, "font mapping", "fonts");
                case ".pgp": return (Ingest, "command aliases", "command-aliases");
                case ".cuix" or ".mnr" or ".mnl" or "._mn": return (Reference, "menu/ribbon/load script -> command catalog", "commands");
                case ".dcl" or ".xpg": return (Reference, "dialog definition / misc support", "dialogs");
            }
        }

        // 10. icons / slides
        if (dir == "hssteel/icons" || dir.StartsWith("hssteel/icons/", StringComparison.Ordinal))
        {
            switch (ext)
            {
                case ".sld": return (Ingest, "slide preview (SLD->SVG)", "slides");
                case ".bmp": return (Ingest, "dialog icon", "icons");
                case ".dcl": return (Ingest, "dialog definition", "dialogs");
            }
        }

        // 11. compiled program / plugins / manuals / memo
        if (dir == "hssteel" && ext is ".vlx" or ".dll" or ".exe")
        {
            return (Reference, "compiled program/plugin: symbol & command catalog only", "fas-catalog");
        }

        if (dir == "hssteel/도움말")
        {
            return (Reference, "manual/example: feature spec, glossary, drafting rules source", "docs");
        }

        if (dir == "hssteel" && ext == ".txt")
        {
            return (Reference, "memo", "docs");
        }

        // 12. quantity / estimate workbooks
        if (lower.StartsWith("hs03/", StringComparison.Ordinal))
        {
            return (Ingest, "quantity/estimate workbook (prices, surcharges, forms; formulas as oracle)", "quantities");
        }

        return (Unknown, "no ledger rule", "-");
    }
}
