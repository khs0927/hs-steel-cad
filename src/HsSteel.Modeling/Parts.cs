using System.Globalization;
using System.Text;
using HsSteel.Domain;

namespace HsSteel.Modeling;

public enum HoleFace { Web, TopFlange, BottomFlange }

/// <summary>Which flange corner is coped (beam end / top-or-bottom).</summary>
public enum CopeCorner { TopStart, BottomStart, TopEnd, BottomEnd }

/// <summary>Flange cope (notch) at a member end: length along the axis, depth from flange tip, scallop radius.</summary>
public sealed record FlangeCope(CopeCorner Corner, double Length, double Depth, double Radius);

/// <summary>A hole on a shape part. X from the part start; Across from the face edge (web: from the bottom, flange: from the near edge).</summary>
public sealed record Hole(HoleFace Face, double X, double Across, double Dia);

/// <summary>A hole in a plate, in plate coordinates (u, v).</summary>
public sealed record PlateHole(double U, double V, double Dia);

/// <summary>A unique part after numbering. Instances refer to it by mark.</summary>
public abstract class Part
{
    public string Mark { get; set; } = "";

    public string Material { get; init; } = "SS275";

    /// <summary>How many of this part exist in the whole project.</summary>
    public int Quantity { get; set; }

    public abstract string Name { get; }

    public abstract double Weight { get; }

    public abstract string Signature { get; }

    protected static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>A cut length of a rolled/built section with its holes.</summary>
public sealed class ShapePart : Part
{
    public required Profile Profile { get; init; }

    public required double Length { get; init; }

    public required IReadOnlyList<Hole> Holes { get; init; }

    /// <summary>Flange copes (scallops) applied for framing into a supporting member.</summary>
    public IReadOnlyList<FlangeCope> Copes { get; init; } = [];

    public override string Name => Profile.Spec;

    public override double Weight => Profile.WeightFor(Length);

    public double PaintArea => Profile.PaintFor(Length);

    public override string Signature
    {
        get
        {
            var sb = new StringBuilder($"S|{Profile.Spec}|{F(Length)}|{Material}");
            foreach (var h in Holes.OrderBy(h => h.Face).ThenBy(h => Math.Round(h.X, 1)).ThenBy(h => Math.Round(h.Across, 1)))
            {
                sb.Append($"|{h.Face}:{F(h.X)}:{F(h.Across)}:{F(h.Dia)}");
            }


            foreach (var c in Copes.OrderBy(c => c.Corner).ThenBy(c => Math.Round(c.Length, 1)).ThenBy(c => Math.Round(c.Depth, 1)))
            {
                sb.Append($"|COPE:{c.Corner}:{F(c.Length)}:{F(c.Depth)}:{F(c.Radius)}");
            }

            return sb.ToString();
        }
    }
}

/// <summary>A flat plate: outline polygon in (u, v), thickness, holes.</summary>
public sealed class PlatePart : Part
{
    public required double Thickness { get; init; }

    public required IReadOnlyList<V2> Outline { get; init; }

    public required IReadOnlyList<PlateHole> Holes { get; init; }

    /// <summary>Role, e.g. "SPLICE-WEB", "SHEAR-TAB", "BASE", "CAP".</summary>
    public string Role { get; init; } = "";

    public double SizeU => Outline.Max(p => p.X) - Outline.Min(p => p.X);

    public double SizeV => Outline.Max(p => p.Y) - Outline.Min(p => p.Y);

    public override string Name => $"PL-{F(Thickness)}x{F(Math.Min(SizeU, SizeV))}x{F(Math.Max(SizeU, SizeV))}";

    public override double Weight => Math.Round(Math.Abs(Area()) * Thickness * 7.85e-6, 2);

    /// <summary>True when the outline is an axis-aligned rectangle starting at the origin.</summary>
    public bool IsRectangle => Outline.Count == 4 && Outline.All(p => (Math.Abs(p.X) < 1e-6 || Math.Abs(p.X - SizeU) < 1e-6) && (Math.Abs(p.Y) < 1e-6 || Math.Abs(p.Y - SizeV) < 1e-6));

    /// <summary>
    /// Identity for numbering. Rectangular plates are compared up to mirroring, so a plate and its
    /// left/right-hand copy get the same mark (it is the same cut piece, turned over).
    /// </summary>
    public override string Signature
    {
        get
        {
            if (!IsRectangle)
            {
                return Describe(Outline, Holes);
            }

            double u = SizeU, v = SizeV;
            var variants = new[]
            {
                Holes,
                Holes.Select(h => h with { U = u - h.U }).ToList(),
                Holes.Select(h => h with { V = v - h.V }).ToList(),
                Holes.Select(h => h with { U = u - h.U, V = v - h.V }).ToList(),
            };
            return variants.Select(hs => Describe(Outline, hs)).Order(StringComparer.Ordinal).First();
        }
    }

    private string Describe(IReadOnlyList<V2> outline, IEnumerable<PlateHole> holes)
    {
        var sb = new StringBuilder($"P|{F(Thickness)}|{Material}");
        foreach (var p in outline)
        {
            sb.Append($"|{F(p.X)},{F(p.Y)}");
        }

        foreach (var h in holes.OrderBy(h => Math.Round(h.U, 1)).ThenBy(h => Math.Round(h.V, 1)))
        {
            sb.Append($"|h{F(h.U)},{F(h.V)},{F(h.Dia)}");
        }

        return sb.ToString();
    }

    private double Area()
    {
        double s = 0;
        for (var i = 0; i < Outline.Count; i++)
        {
            var a = Outline[i];
            var b = Outline[(i + 1) % Outline.Count];
            s += (a.X * b.Y) - (b.X * a.Y);
        }

        return s / 2;
    }

    public static PlatePart Rect(double t, double u, double v, IEnumerable<PlateHole> holes, string role, string material) => new()
    {
        Thickness = t,
        Outline = [new(0, 0), new(u, 0), new(u, v), new(0, v)],
        Holes = holes.ToList(),
        Role = role,
        Material = material,
    };
}

/// <summary>Plate plane relative to the main member: Web = x-y plane, Flange = x-z plane, End = y-z plane.</summary>
public enum PlateOrientation { Web, Flange, End }

/// <summary>
/// Where a plate sits in the main member's local frame (x along, y depth, z width).
/// Web: (u,v)→(x,y), thickness along z·NSign. Flange: (u,v)→(x,z), thickness along y·NSign.
/// End: (u,v)→(z·USign, y), thickness along x·NSign.
/// </summary>
public sealed record Placement(PlateOrientation Orientation, V3 Origin, int USign = 1, int NSign = 1)
{
    public V3 Map(double u, double v) => Orientation switch
    {
        PlateOrientation.Web => Origin + new V3(u, v, 0),
        PlateOrientation.Flange => Origin + new V3(u, 0, v),
        _ => Origin + new V3(0, v, USign * u),
    };

    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Orientation}:{Origin.X:0.#},{Origin.Y:0.#},{Origin.Z:0.#}:{USign}:{NSign}");
}

/// <summary>A part attached to an assembly's main member. Loose parts (splice plates) are shipped with the assembly.</summary>
public sealed record Attachment(PlatePart Part, Placement At, bool Welded);

/// <summary>Bolt count by set name ("TS M20"); <paramref name="Length"/> is the bolt length in mm (0 = unknown).</summary>
public sealed record BoltSet(string Name, int Count, double Length = 0)
{
    /// <summary>Name with length for notes and the BOM, e.g. "TS M20x75".</summary>
    public string Label => StandardOptions.BoltLabel(Name, Length);
}

/// <summary>A unique shipping assembly after numbering.</summary>
public sealed class Assembly
{
    public string Mark { get; set; } = "";

    public required AssemblyType Type { get; init; }

    public required ShapePart Main { get; init; }

    public required IReadOnlyList<Attachment> Attachments { get; init; }

    public required IReadOnlyList<BoltSet> Bolts { get; init; }

    /// <summary>Member ids built as this assembly.</summary>
    public List<string> Members { get; } = [];

    public int Quantity => Members.Count;

    public double Weight => Math.Round(Main.Weight + Attachments.Sum(a => a.Part.Weight), 1);

    public string Signature =>
        $"{Type}|{Main.Mark}|" + string.Join("|", Attachments.Select(a => $"{a.Part.Mark}@{a.At.Key}:{a.Welded}").Order(StringComparer.Ordinal));
}
