using System.Globalization;
using HsSteel.Assets;

namespace HsSteel.Domain;

/// <summary>H / BH section geometry in mm.</summary>
public sealed record HSection(string Spec, double H, double B, double Tw, double Tf, double R, double UnitWeight)
{
    public static HSection From(SectionRecord r) => new(r.Spec, r.M[0], r.M[1], r.M[2], r.M[3], r.M[4], r.UnitWeight);

    /// <summary>Parses "H300x150x6.5x9" (or "BH...") when no table row exists; weight from steel density.</summary>
    public static HSection Parse(string spec)
    {
        var body = spec.TrimStart('B', 'H', 'b', 'h', '-');
        var d = body.Split('x', 'X', '*').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        if (d.Length < 4)
        {
            throw new FormatException($"H section spec needs HxBxTwxTf: '{spec}'");
        }

        var area = (2 * d[1] * d[3]) + ((d[0] - (2 * d[3])) * d[2]); // mm², fillets ignored
        return new HSection(spec, d[0], d[1], d[2], d[3], 0, Math.Round(area * 7.85e-3, 2));
    }
}

/// <summary>A bolt group on one member end, on the web or flanges.</summary>
/// <param name="Face">"web" or "flange".</param>
/// <param name="FromEnd">Distance from the member end to the first row (end gauge).</param>
/// <param name="Rows">Rows along the member axis.</param>
/// <param name="Pitch">Spacing between rows.</param>
/// <param name="Lines">Bolt lines across the face.</param>
/// <param name="Gauge">Spacing between lines.</param>
/// <param name="HoleDia">Hole diameter.</param>
public sealed record BoltGroup(string Face, double FromEnd, int Rows, double Pitch, int Lines, double Gauge, double HoleDia);

public enum MemberEnd { Start, End }

/// <summary>A straight H member to be detailed.</summary>
public sealed record Member(
    string Mark,
    HSection Section,
    double Length,
    int Quantity = 1,
    string Material = "SS275",
    IReadOnlyList<(MemberEnd End, BoltGroup Group)>? Holes = null,
    bool Scallop = false)
{
    public double Weight => Math.Round(Section.UnitWeight * Length / 1000.0, 1);
}

/// <summary>Detailing defaults taken from HS-STEEL Project.dat.</summary>
public sealed record DetailRules(double Scallop = 30, double EndGauge = 40, double HoleDia = 22, double TextHeight = 2.5, double DimGap = 8)
{
    public static DetailRules From(IReadOnlyDictionary<string, string> p)
    {
        double Get(string k, double d) => p.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : d;
        return new DetailRules(Get("SCALLOP", 30), Get("ENDGAGE", 40), Get("SHOLE", 22));
    }
}
