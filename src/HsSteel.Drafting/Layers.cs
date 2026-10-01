namespace HsSteel.Drafting;

/// <summary>Layer standard for generated shop drawings.</summary>
public static class Layers
{
    public const string Outline = "HS-OUTLINE";
    public const string Hidden = "HS-HIDDEN";
    public const string Center = "HS-CENTER";
    public const string Hole = "HS-HOLE";
    public const string Dim = "HS-DIM";
    public const string Text = "HS-TEXT";
    public const string Break = "HS-BREAK";
    public const string Frame = "HS-FRAME";

    /// <summary>(name, ACI color, linetype) — created before drawing.</summary>
    public static readonly (string Name, short Color, string Linetype)[] All =
    [
        (Outline, 7, "Continuous"), (Hidden, 8, "HIDDEN"), (Center, 1, "CENTER"), (Hole, 4, "Continuous"),
        (Dim, 3, "Continuous"), (Text, 2, "Continuous"), (Break, 6, "Continuous"), (Frame, 7, "Continuous"),
    ];
}
