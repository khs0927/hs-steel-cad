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
/// <param name="TextHeight">Paper text height (mm) of notes and marks.</param>
/// <param name="DimGap">Paper distance between dimension rows (mm).</param>
/// <param name="ConnectionGap">Clearance between a beam end and the supporting web (mm).</param>
/// <param name="DimTextHeight">
/// Paper height (mm) of dimension text (DFT-001): the template 새공사-2019.dwg dimension styles DIM-100 and Standard
/// both use text 3.4 / arrow 3 / gap 1 / baseline step 5 (at DIMSCALE 50). Model height = this × view scale.
/// Project.dat m32-dim-txt-box (3) is not used: the template is the canonical source.
/// </param>
public sealed record DetailRules(
    double Scallop = 30, double EndGauge = 40, double HoleDia = 22, double WeldGap = 5, string Material = "SS275",
    double TextHeight = 2.5, double DimGap = 7, double ConnectionGap = 10, double DimTextHeight = 3.4)
{
    /// <summary>Template (DIM-100 / Standard) dimension text height on paper, mm (DFT-001).</summary>
    public const double TemplateDimText = 3.4;

    /// <summary>
    /// Assembly mark heads overriding <see cref="AssemblyTypes.Prefix(AssemblyType)"/> (NUM-001), normally read from the
    /// legacy Numbering.dat (M83-*-HD-BOX). Null = the Numbering.dat defaults built into <see cref="AssemblyTypes"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<AssemblyType, string>? MarkHeads { get; init; }

    /// <summary>Rules from Project.dat keys; Numbering.dat keys (M83-*-HD-BOX) may be merged into the same map.</summary>
    public static DetailRules From(IReadOnlyDictionary<string, string> p)
    {
        double Get(string k, double d) =>
            p.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : d;
        var material = p.TryGetValue("SWS", out var m) && m.Length > 0 ? m : "SS275";
        var heads = AssemblyTypes.HeadsFrom(p);
        return new DetailRules(Get("SCALLOP", 30), Get("ENDGAGE", 40), Get("SHOLE", 22), Get("WDGAP", 5), material)
        {
            MarkHeads = heads.Count > 0 ? heads : null,
        };
    }
}

/// <summary>Assembly types, as classified by HS-STEEL from the centre-line layer CL-3D-&lt;TYPE&gt;.</summary>
public enum AssemblyType { Column, SubColumn, Post, Girder, Beam, CraneGirder, Brace, Purlin, Girth, Rafter, Truss, Stair, HandRail, Embed, Other }

public static class AssemblyTypes
{
    /// <summary>
    /// Mark prefix used for assembly numbering (NUM-001): the heads of the legacy new-project Numbering.dat
    /// (M83-COLUMN/SUBCOL/POST-HD-BOX C001, RAFTER/TRUSS/CRANEG/GIRDER G001, BEAM B001, BRACE R001, PURLIN PU001,
    /// GIRTH GT001, STAIR S001, HANDRL H001, NGNGNG X001). Embed has no Numbering.dat head and keeps EM.
    /// </summary>
    public static string Prefix(AssemblyType t) => t switch
    {
        AssemblyType.Column or AssemblyType.SubColumn or AssemblyType.Post => "C",
        AssemblyType.Girder or AssemblyType.Rafter or AssemblyType.Truss or AssemblyType.CraneGirder => "G",
        AssemblyType.Beam => "B",
        AssemblyType.Brace => "R",
        AssemblyType.Purlin => "PU",
        AssemblyType.Girth => "GT",
        AssemblyType.Stair => "S",
        AssemblyType.HandRail => "H",
        AssemblyType.Embed => "EM",
        _ => "X",
    };

    /// <summary>Mark prefix with project overrides (e.g. from Numbering.dat via <see cref="HeadsFrom"/>).</summary>
    public static string Prefix(AssemblyType t, IReadOnlyDictionary<AssemblyType, string>? heads) =>
        heads is not null && heads.TryGetValue(t, out var h) && h.Length > 0 ? h : Prefix(t);

    /// <summary>Numbering.dat head keys (M83-&lt;KEY&gt;-HD-BOX, dashes padded to 6 chars) for each engine type.</summary>
    public static IReadOnlyDictionary<AssemblyType, string> NumberingKeys { get; } = new Dictionary<AssemblyType, string>
    {
        [AssemblyType.Column] = "M83-COLUMN-HD-BOX",
        [AssemblyType.SubColumn] = "M83-SUBCOL-HD-BOX",
        [AssemblyType.Post] = "M83-POST---HD-BOX",
        [AssemblyType.Rafter] = "M83-RAFTER-HD-BOX",
        [AssemblyType.Truss] = "M83-TRUSS--HD-BOX",
        [AssemblyType.CraneGirder] = "M83-CRANEG-HD-BOX",
        [AssemblyType.Girder] = "M83-GIRDER-HD-BOX",
        [AssemblyType.Beam] = "M83-BEAM---HD-BOX",
        [AssemblyType.Brace] = "M83-BRACE--HD-BOX",
        [AssemblyType.Purlin] = "M83-PURLIN-HD-BOX",
        [AssemblyType.Girth] = "M83-GIRTH--HD-BOX",
        [AssemblyType.Stair] = "M83-STAIR--HD-BOX",
        [AssemblyType.HandRail] = "M83-HANDRL-HD-BOX",
        [AssemblyType.Other] = "M83-NGNGNG-HD-BOX",
    };

    /// <summary>
    /// Mark heads found in Numbering.dat settings: "C001" -&gt; "C" (the trailing start number is dropped).
    /// Types whose key is missing or blank are left out (they fall back to <see cref="Prefix(AssemblyType)"/>).
    /// </summary>
    public static IReadOnlyDictionary<AssemblyType, string> HeadsFrom(IReadOnlyDictionary<string, string> numbering)
    {
        var heads = new Dictionary<AssemblyType, string>();
        foreach (var (t, key) in NumberingKeys)
        {
            if (numbering.TryGetValue(key, out var v))
            {
                var head = v.Trim().TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                if (head.Length > 0)
                {
                    heads[t] = head;
                }
            }
        }

        return heads;
    }

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

    public Profile Resolve(string spec)
    {
        if (_rows.TryGetValue(spec, out var r))
        {
            return Profile.FromRecord(r);
        }

        // WT-002 fallback: a rolled H/I, channel or angle not in the table borrows the root radius of the nearest
        // tabulated section of the same kind, so its (4−π)·r² fillet term is a catalog value, not an estimate.
        var p = Profile.Parse(spec);
        var root = NearestRootRadius(p);
        return root is { } rr && Math.Abs(rr - p.RootRadius) > 1e-9 ? Profile.Parse(spec, rr) : p;
    }

    /// <summary>Root radius of the tabulated rolled section of the same kind closest in depth + width (null if none).</summary>
    public double? NearestRootRadius(Profile p)
    {
        if (!p.IsRolled || p.Kind is not (ShapeKind.I or ShapeKind.L or ShapeKind.Channel))
        {
            return null;
        }

        double? best = null;
        var bestDist = double.MaxValue;
        foreach (var row in _rows.Values)
        {
            var q = Profile.FromRecord(row);
            if (q.Kind != p.Kind || !q.IsRolled || q.RootRadius <= 0)
            {
                continue;
            }

            var dist = Math.Abs(q.Depth - p.Depth) + Math.Abs(q.Width - p.Width);
            if (dist < bestDist)
            {
                (best, bestDist) = (q.RootRadius, dist);
            }
        }

        return best;
    }
}
