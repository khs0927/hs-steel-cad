using System.Globalization;
using System.Text.Json.Serialization;
using HsSteel.Assets;

namespace HsSteel.Domain;

/// <summary>2D point/vector in mm.</summary>
public readonly record struct V2(double X, double Y)
{
    public static V2 operator +(V2 a, V2 b) => new(a.X + b.X, a.Y + b.Y);

    public static V2 operator -(V2 a, V2 b) => new(a.X - b.X, a.Y - b.Y);

    public static V2 operator *(V2 a, double k) => new(a.X * k, a.Y * k);

    [JsonIgnore]
    public double Length => Math.Sqrt((X * X) + (Y * Y));
}

/// <summary>3D point in model space (mm). Z is up.</summary>
public readonly record struct V3(double X, double Y, double Z)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

    [JsonIgnore]
    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    [JsonIgnore]
    public V3 Unit => this * (1 / Length);

    public static double Dot(V3 a, V3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:0.#},{Y:0.#},{Z:0.#})");
}

/// <summary>Detailing defaults (HS-STEEL Project.dat keys in brackets).</summary>
/// <param name="Scallop">Scallop radius [SCALLOP].</param>
/// <param name="EndGauge">End distance to first bolt row [ENDGAGE].</param>
/// <param name="HoleDia">Default hole diameter [SHOLE].</param>
/// <param name="WeldGap">Gap for welded fit-up [WDGAP].</param>
/// <param name="Material">Default steel grade [SWS].</param>
/// <param name="TextHeight">Paper text height (mm).</param>
/// <param name="DimGap">Paper distance between dimension rows (mm).</param>
/// <param name="ConnectionGap">Clearance between a beam end and the supporting web (mm).</param>
public sealed record DetailRules(
    double Scallop = 30, double EndGauge = 40, double HoleDia = 22, double WeldGap = 5, string Material = "SS275",
    double TextHeight = 2.5, double DimGap = 7, double ConnectionGap = 10)
{
    public static DetailRules From(IReadOnlyDictionary<string, string> p)
    {
        double Get(string k, double d) =>
            p.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : d;
        var material = p.TryGetValue("SWS", out var m) && m.Length > 0 ? m : "SS275";
        return new DetailRules(Get("SCALLOP", 30), Get("ENDGAGE", 40), Get("SHOLE", 22), Get("WDGAP", 5), material);
    }
}

/// <summary>Assembly types, as classified by HS-STEEL from the centre-line layer CL-3D-&lt;TYPE&gt;.</summary>
public enum AssemblyType { Column, SubColumn, Post, Girder, Beam, CraneGirder, Brace, Purlin, Girth, Rafter, Truss, Stair, HandRail, Embed, Other }

public static class AssemblyTypes
{
    /// <summary>Mark prefix used for assembly numbering.</summary>
    public static string Prefix(AssemblyType t) => t switch
    {
        AssemblyType.Column => "C",
        AssemblyType.SubColumn => "SC",
        AssemblyType.Post => "PT",
        AssemblyType.Girder => "G",
        AssemblyType.Beam => "B",
        AssemblyType.CraneGirder => "CG",
        AssemblyType.Brace => "BR",
        AssemblyType.Purlin => "PU",
        AssemblyType.Girth => "GT",
        AssemblyType.Rafter => "RF",
        AssemblyType.Truss => "TR",
        AssemblyType.Stair => "ST",
        AssemblyType.HandRail => "HR",
        AssemblyType.Embed => "EM",
        _ => "X",
    };

    /// <summary>HS-STEEL centre-line layer name for the type (used by the drawing recogniser).</summary>
    public static string CenterLineLayer(AssemblyType t) => t switch
    {
        AssemblyType.SubColumn => "CL-3D-SUBCOL",
        AssemblyType.CraneGirder => "CL-3D-CRANE_GIRDER",
        AssemblyType.HandRail => "CL-3D-HAND_RAIL",
        AssemblyType.Other => "CL-3D",
        _ => "CL-3D-" + t.ToString().ToUpperInvariant(),
    };

    public static AssemblyType FromLayer(string layer)
    {
        foreach (var t in Enum.GetValues<AssemblyType>())
        {
            if (string.Equals(CenterLineLayer(t), layer, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }

        return layer.Equals("CL-3D-FAFTER", StringComparison.OrdinalIgnoreCase) ? AssemblyType.Rafter : AssemblyType.Other;
    }
}

/// <summary>Section catalogue loaded from the legacy attributes folder (or empty: profiles are then parsed from the spec).</summary>
public sealed class SectionCatalog
{
    private readonly Dictionary<string, SectionRecord> _rows = new(StringComparer.OrdinalIgnoreCase);

    public static SectionCatalog Empty { get; } = new();

    public IReadOnlyCollection<SectionRecord> Rows => _rows.Values;

    public static SectionCatalog Load(string attributesDir)
    {
        var c = new SectionCatalog();
        if (!Directory.Exists(attributesDir))
        {
            return c;
        }

        foreach (var f in Directory.GetFiles(attributesDir, "*.dat"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (name.Equals("Project", StringComparison.OrdinalIgnoreCase) || name.StartsWith("SCSS", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var r in SectionTable.Load(f))
            {
                c._rows.TryAdd(r.Spec, r);
            }
        }

        return c;
    }

    public Profile Resolve(string spec) =>
        _rows.TryGetValue(spec, out var r) ? Profile.FromRecord(r) : Profile.Parse(spec);
}
