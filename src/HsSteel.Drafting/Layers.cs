namespace HsSteel.Drafting;

/// <summary>Layer standard for generated shop drawings.</summary>
public static class Layers
{
    public const string Outline = "HS-OUTLINE";
    public const string Hidden = "HS-HIDDEN";
    public const string Center = "HS-CENTER";
    public const string Hole = "HS-HOLE";
    public const string Plate = "HS-PLATE";
    public const string Loose = "HS-LOOSE";
    public const string Weld = "HS-WELD";
    public const string Dim = "HS-DIM";
    public const string Text = "HS-TEXT";
    public const string Mark = "HS-MARK";
    public const string Table = "HS-TABLE";
    public const string Grid = "HS-GRID";
    public const string Member = "HS-MEMBER";
    public const string Break = "HS-BREAK";
    public const string Frame = "HS-FRAME";

    /// <summary>(name, ACI color, linetype) — created before drawing.</summary>
    public static readonly (string Name, short Color, string Linetype)[] All =
    [
        (Outline, 7, "Continuous"), (Hidden, 8, "HIDDEN"), (Center, 1, "CENTER"), (Hole, 4, "Continuous"),
        (Plate, 5, "Continuous"), (Loose, 30, "DASHED"), (Weld, 6, "Continuous"), (Dim, 3, "Continuous"),
        (Text, 2, "Continuous"), (Mark, 2, "Continuous"), (Table, 7, "Continuous"), (Grid, 1, "CENTER"),
        (Member, 7, "Continuous"), (Break, 6, "Continuous"), (Frame, 7, "Continuous"),
    ];
}
