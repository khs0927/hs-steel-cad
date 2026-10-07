using System.Security.Cryptography;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;

namespace HsSteel.Assets.Blocks;

public sealed record BlockAttributeDef(string Tag, string Prompt, string Default);

public sealed record BlockAsset(
    string Name,
    string SourcePath,
    string Sha256,
    double[] BasePoint,
    double[] Extents,
    IReadOnlyList<BlockAttributeDef> Attributes,
    int EntityCount,
    IReadOnlyList<string> Layers,
    string? Description);

public sealed record BlockLoadFailure(string Path, string Reason);

public sealed record BlockCatalogResult(IReadOnlyList<BlockAsset> Assets, IReadOnlyList<BlockLoadFailure> Failures);

/// <summary>Loads legacy block DWG files; each DWG is one block asset (model space contents).</summary>
public static class BlockCatalog
{
    private static readonly object FailLock = new();
    private static List<BlockLoadFailure> _failures = new();

    /// <summary>Failures from the most recent <see cref="Load"/> call.</summary>
    public static IReadOnlyList<BlockLoadFailure> Failures { get { lock (FailLock) return _failures.ToArray(); } }

    public static IReadOnlyList<BlockAsset> Load(string blockDir) => LoadWithFailures(blockDir).Assets;

    public static BlockCatalogResult LoadWithFailures(string blockDir)
    {
        var assets = new List<BlockAsset>();
        var failures = new List<BlockLoadFailure>();
        if (Directory.Exists(blockDir))
        {
            var files = Directory.EnumerateFiles(blockDir, "*.dwg", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                try { assets.Add(LoadOne(f)); }
                catch (Exception ex) { failures.Add(new BlockLoadFailure(f, ex.GetType().Name + ": " + ex.Message)); }
            }
        }
        assets.Sort((a, b) =>
        {
            int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a.SourcePath, b.SourcePath);
        });
        lock (FailLock) _failures = failures;
        return new BlockCatalogResult(assets, failures);
    }

    public static BlockAsset LoadOne(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var doc = ReadDoc(path);
        var ents = doc.ModelSpace.Entities.ToList();
        var ext = new Extents2();
        var layers = new SortedSet<string>(StringComparer.Ordinal);
        var attrs = new List<BlockAttributeDef>();
        foreach (var e in ents)
        {
            if (e.Layer?.Name is { } ln) layers.Add(ln);
            if (e is AttributeDefinition ad)
                attrs.Add(new BlockAttributeDef(ad.Tag ?? "", ad.Prompt ?? "", ad.Value ?? ""));
        }
        BlockGeometry.Accumulate(ents, ext);
        var ip = doc.Header.ModelSpaceInsertionBase;
        var extents = ext.IsEmpty ? new[] { 0.0, 0, 0, 0 } : new[] { ext.MinX, ext.MinY, ext.MaxX, ext.MaxY };
        var desc = ents.OfType<MText>().Select(t => t.Value)
            .Concat(ents.OfType<TextEntity>().Select(t => t.Value))
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        return new BlockAsset(Path.GetFileNameWithoutExtension(path), path, sha,
            new[] { Fin(ip.X), Fin(ip.Y), Fin(ip.Z) }, extents,
            attrs, ents.Count, layers.ToList(), desc);
    }

    internal static CadDocument ReadDoc(string path)
    {
        using var r = new DwgReader(path);
        return r.Read();
    }

    private static double Fin(double v) => double.IsFinite(v) ? v : 0;
}

internal sealed class Extents2
{
    public double MinX = double.MaxValue, MinY = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue;
    public bool IsEmpty => MinX > MaxX;
    public void Add(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        if (x < MinX) MinX = x;
        if (x > MaxX) MaxX = x;
        if (y < MinY) MinY = y;
        if (y > MaxY) MaxY = y;
    }
}
