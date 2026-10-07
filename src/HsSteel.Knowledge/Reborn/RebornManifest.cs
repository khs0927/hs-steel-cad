using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HsSteel.Knowledge.Reborn;

/// <summary>One file of C:\HS-STEEL_REBORN. Sha256 is null only for bulk-excluded virtualenv files.</summary>
public sealed record RebornEntry(string RelPath, long Size, string? Sha256, string Disposition, string Reason, string Category);

/// <summary>
/// Deterministic ledger of the prior analysis project (derived, partly-unverified knowledge about legacy HS-STEEL).
/// Every file on disk gets exactly one row; rows are ordinal by relPath; no timestamps.
/// </summary>
public sealed record RebornManifest(string Schema, string RootName, int FileCount, IReadOnlyDictionary<string, int> Counts, IReadOnlyList<RebornEntry> Files)
{
    public const string SchemaId = "hs-reborn-manifest/1";
    public const string DefaultRoot = @"C:\HS-STEEL_REBORN";

    /// <summary>Files whose content is loaded into derived_spec / doc_chunk (trust is assigned in <see cref="RebornTrust"/>).</summary>
    public static readonly IReadOnlySet<string> IngestFiles = new SortedSet<string>(StringComparer.Ordinal)
    {
        "ARCHITECTURE.md", "ORCHESTRATION_PLAN.md", "orch/user_manual.md",
        "orch/bom_schema.json", "orch/bomlist_spec.json", "orch/plate_spec.json", "orch/cut_plan_spec.json", "orch/plot_spec.json",
        "orch/quote_logic.json", "orch/fas_symbols.json", "orch/fas_symbols_summary.json", "orch/command_triage.json", "orch/command_triage_v2.json",
        "orch/dat_full.json", "orch/dwg_blocks.json", "orch/dwg_blocks_v2.json", "orch/ui_cuix_spec.json", "orch/template_manifest.json",
        "orch/net_asm.json", "orch/lisp_dialect.json", "orch/weight_audit.json", "orch/spec_coverage_v2.json",
        "orch/_integration.json", "orch/_integration_20260917_snapshot.json", "orch/zwcad_stability.json",
        "catalog/commands.json", "catalog/catalog_summary.json", "catalog/feature_groups.json",
        "work/fas_strings.json", "work/fas_module_index.json",
        "materials/weight_table.json", "materials/scss_bolt_tables.json", "materials/estimate_2017.json",
    };

    private static readonly string[] ProbePrefixes =
    [
        "com_probe", "focus", "uia", "listwins", "find_cmdline", "find_edit", "fix_dcom", "shot", "sendcmd", "windows", "mods", "chk",
        "diag_zwcad", "dump_dlg", "final_com", "install_driver", "oda_test", "verify_b", "verify_c", "ezdxf_test", "test_lisp_", "screen",
        "lisp_engine_ok",
    ];

    private static readonly string[] BinaryExt = [".exe", ".dll", ".pyd", ".so", ".lib", ".a", ".dylib", ".whl", ".fas"];

    public static readonly JsonSerializerOptions JsonOptions = Manifest.JsonOptions;

    /// <summary>One row per line so the committed ledger diffs line-by-line.</summary>
    public string ToJson()
    {
        var o = new JsonSerializerOptions(JsonOptions) { WriteIndented = false };
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"schema\": ").Append(JsonSerializer.Serialize(Schema, o)).Append(",\n");
        sb.Append("  \"rootName\": ").Append(JsonSerializer.Serialize(RootName, o)).Append(",\n");
        sb.Append("  \"fileCount\": ").Append(FileCount).Append(",\n");
        sb.Append("  \"counts\": ").Append(JsonSerializer.Serialize(Counts, o)).Append(",\n");
        sb.Append("  \"files\": [\n");
        for (var i = 0; i < Files.Count; i++)
        {
            sb.Append("    ").Append(JsonSerializer.Serialize(Files[i], o)).Append(i + 1 < Files.Count ? ",\n" : "\n");
        }

        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    public static RebornManifest Load(string path) =>
        JsonSerializer.Deserialize<RebornManifest>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("empty reborn manifest");

    public void Save(string path) => File.WriteAllText(path, ToJson(), new UTF8Encoding(false));

    /// <summary>All files under root as ('/'-separated relPath, size), ordinal. Sizes come from the directory entry so odd names ('nul', '"') work.</summary>
    public static List<(string RelPath, long Size)> ListFiles(string root)
    {
        var full = Path.GetFullPath(root).TrimEnd('\\', '/');
        var e = new System.IO.Enumeration.FileSystemEnumerable<(string, long)>(full,
            (ref System.IO.Enumeration.FileSystemEntry x) => (x.ToFullPath()[(full.Length + 1)..].Replace('\\', '/'), x.Length),
            new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false })
        {
            // 17 directory symlinks under engine/out point back into the tree; don't follow them (matches `find -type f`).
            ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry x) => !x.IsDirectory && (x.Attributes & FileAttributes.ReparsePoint) == 0,
            ShouldRecursePredicate = (ref System.IO.Enumeration.FileSystemEntry x) => (x.Attributes & FileAttributes.ReparsePoint) == 0,
        };
        return e.OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();
    }

    public static RebornManifest Scan(string root)
    {
        var full = Path.GetFullPath(root).TrimEnd('\\', '/');
        var rows = new List<RebornEntry>();
        foreach (var (rel, size) in ListFiles(full))
        {
            var (disp, reason, cat) = Classify(rel);
            string? sha = null;
            if (reason == DeviceNameReason)
            {
                // Win32 maps 'nul' to the NUL device even with a long-path prefix — content is unreadable through the Win32 API.
            }
            else if (reason != VenvReason)
            {
                sha = Sha256(Path.Combine(full, rel.Replace('/', Path.DirectorySeparatorChar)));
            }

            rows.Add(new RebornEntry(rel, size, sha, disp, reason, cat));
        }

        var counts = rows.GroupBy(r => r.Disposition).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
        return new RebornManifest(SchemaId, Path.GetFileName(full), rows.Count, counts, rows);
    }

    public const string DeviceNameReason = "win32 device name 'nul' (shell-redirect artifact; unreadable, sha256 null)";

    public const string VenvReason = "venv bulk-excluded";

    public static string Sha256(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }

    /// <summary>Category = top-level folder (or root-doc / root-script / root-other for top-level files).</summary>
    public static string Category(string rel)
    {
        var slash = rel.IndexOf('/');
        if (slash > 0)
        {
            return rel[..slash];
        }

        var ext = Path.GetExtension(rel).ToLowerInvariant();
        return ext switch
        {
            ".md" => "root-doc",
            ".py" or ".ps1" or ".lsp" => "root-script",
            _ => "root-other",
        };
    }

    public static (string Disposition, string Reason, string Category) Classify(string rel)
    {
        var cat = Category(rel);
        var lower = rel.ToLowerInvariant();
        var segs = lower.Split('/');
        var name = segs[^1];
        var ext = Path.GetExtension(name);
        const string Ex = ManifestScanner.Excluded;
        const string Ref = ManifestScanner.Reference;

        if (segs[0] == ".venv")
        {
            return (Ex, VenvReason, cat);
        }

        if (IngestFiles.Contains(rel))
        {
            return (ManifestScanner.Ingest, "derived spec loaded into derived_spec/doc_chunk with trust grade", cat);
        }

        if (segs.Contains("site-packages") || segs.Contains("node_modules"))
        {
            return (Ex, "vendored third-party package", cat);
        }

        if (segs.Contains("__pycache__") || ext == ".pyc")
        {
            return (Ex, "python bytecode cache", cat);
        }

        if (segs.Contains(".pytest_cache"))
        {
            return (Ex, "pytest cache", cat);
        }

        if (name == "nul")
        {
            return (Ex, DeviceNameReason, cat);
        }

        if (name == "\"" || segs.Contains("\""))
        {
            return (Ex, "shell-redirect artifact (stray nul/quote name)", cat);
        }

        if (BinaryExt.Contains(ext))
        {
            return ext == ".fas"
                ? (Ref, "extracted compiled LISP module (symbols already in orch/fas_symbols.json)", cat)
                : (Ex, "tool binary", cat);
        }

        if (segs.Length == 1 && ProbePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
        {
            return (Ex, "scratch probe script/output (ZWCAD/COM/UIA experiments)", cat);
        }

        if (ext is ".log" or ".err" || name.EndsWith(".prev_attempt", StringComparison.Ordinal) || segs.Contains("tmp") || name.StartsWith("tmp_", StringComparison.Ordinal))
        {
            return (Ex, "run log / scratch output", cat);
        }

        if (cat == "_docs_backup_20260926")
        {
            return (Ref, "pre-correction originals (superseded by corrected docs)", cat);
        }

        if (cat == "engine" && segs.Length > 1 && segs[1] == "out")
        {
            return (Ref, "engine run output / mutation-test artifact", cat);
        }

        return ext switch
        {
            ".py" or ".ps1" or ".lsp" or ".cmd" or ".bat" or ".sh" => (Ref, "analysis/engine source (behaviour reference, not ported)", cat),
            ".md" or ".txt" => (Ref, "analysis note (derived, unverified)", cat),
            ".json" or ".csv" or ".jsonl" => (Ref, "derived data (not ingested)", cat),
            ".dxf" or ".dwg" => (Ref, "converted drawing / probe output", cat),
            ".png" or ".jpg" or ".svg" or ".pdf" => (Ref, "rendered output / figure", cat),
            ".xlsx" or ".xlsm" => (Ref, "workbook output / fixture", cat),
            _ => (Ref, "other derived file", cat),
        };
    }
}
