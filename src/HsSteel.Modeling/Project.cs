using System.Text.Json;
using System.Text.Json.Serialization;
using HsSteel.Domain;

namespace HsSteel.Modeling;

/// <summary>A grid line: X grids are vertical lines at X = Position, Y grids horizontal at Y = Position.</summary>
public sealed record GridLine(string Name, double Position);

public sealed record Level(string Name, double Elevation);

/// <summary>A member on its centre line (start → end, mm). Roll rotates the section about its axis (degrees).</summary>
public sealed record MemberDef(string Id, AssemblyType Type, string Section, V3 Start, V3 End, double Roll = 0, string? Material = null)
{
    [JsonIgnore]
    public double AxisLength => (End - Start).Length;
}

public enum MemberEnd { Start, End }

/// <summary>Connections that generate cuts, holes and plates. Members are referenced by id.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SpliceDef), "splice")]
[JsonDerivedType(typeof(ShearTabDef), "shear_tab")]
[JsonDerivedType(typeof(BasePlateDef), "base_plate")]
[JsonDerivedType(typeof(EndCapDef), "end_cap")]
public abstract record ConnectionDef(string Id);

/// <summary>Bolted H splice: <paramref name="MemberA"/>'s end meets <paramref name="MemberB"/>'s start (SCSS standard).</summary>
public sealed record SpliceDef(string Id, string MemberA, string MemberB, int BoltSize = 0) : ConnectionDef(Id);

/// <summary>
/// Single-plate shear connection (HS-STEEL "GIRDER ← BEAM 1면마찰 GUSSET"): plate welded to the support web,
/// beam web bolted. The beam end is cut back to the support web face + gap.
/// </summary>
public sealed record ShearTabDef(string Id, string Beam, MemberEnd BeamEnd, string Support, double PlateT = 0, int BoltSize = 0) : ConnectionDef(Id);

/// <summary>Column base plate with four anchor holes (welded to the column start).</summary>
public sealed record BasePlateDef(string Id, string Column, double Thickness = 25, double Margin = 100, double AnchorDia = 27, double AnchorEdge = 50) : ConnectionDef(Id);

/// <summary>Cap plate welded on a member end.</summary>
public sealed record EndCapDef(string Id, string Member, MemberEnd End, double Thickness = 12) : ConnectionDef(Id);

/// <summary>Model input: everything needed to generate shop drawings.</summary>
public sealed class Project
{
    public string Name { get; set; } = "PROJECT";

    /// <summary>Date written in title blocks (kept in the model so output is deterministic).</summary>
    public string Date { get; set; } = "";

    public List<GridLine> GridX { get; set; } = [];

    public List<GridLine> GridY { get; set; } = [];

    public List<Level> Levels { get; set; } = [];

    public List<MemberDef> Members { get; set; } = [];

    public List<ConnectionDef> Connections { get; set; } = [];

    public DetailRules? Rules { get; set; }

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Project FromJson(string json) => JsonSerializer.Deserialize<Project>(json, Json) ?? throw new InvalidDataException("Empty project.");

    public MemberDef Member(string id) =>
        Members.FirstOrDefault(m => m.Id == id) ?? throw new KeyNotFoundException($"Member '{id}' not found.");

    public double Grid(string name) =>
        GridX.Concat(GridY).FirstOrDefault(g => g.Name == name)?.Position ?? throw new KeyNotFoundException($"Grid '{name}' not found.");

    public double Elevation(string name) =>
        Levels.FirstOrDefault(l => l.Name == name)?.Elevation ?? throw new KeyNotFoundException($"Level '{name}' not found.");
}
