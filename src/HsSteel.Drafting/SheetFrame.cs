namespace HsSteel.Drafting;

/// <summary>Where a title-block field is written: frame coordinates (paper mm) and text height.</summary>
public sealed record TextSlot(double X, double Y, double Height, string Justify = "middle_left");

/// <summary>
/// The company sheet border (도곽). It is the company's own block: we need its size at 1:1, the rectangle
/// where views may be placed (excluding title block / notes), and where to write the sheet fields.
/// Fields go into block attributes (<see cref="AttributeTags"/>: field → tag) when the block has them,
/// otherwise as text at <see cref="TextSlots"/>. Everything is modelled 1:1 in mm and the frame is
/// inserted scaled by the sheet scale (HS-STEEL practice: frame × SCL in model space).
/// </summary>
/// <param name="BlockName">Block name of the frame in the target drawing.</param>
/// <param name="Width">Frame width at 1:1 (paper mm).</param>
/// <param name="Height">Frame height at 1:1.</param>
/// <param name="AreaX">Left of the usable drawing area, relative to the frame insertion point.</param>
/// <param name="AreaY">Bottom of the usable area.</param>
/// <param name="AreaW">Usable width.</param>
/// <param name="AreaH">Usable height.</param>
public sealed record SheetFrame(string BlockName, double Width, double Height, double AreaX, double AreaY, double AreaW, double AreaH)
{
    /// <summary>Optional DWG/DXF that defines the frame block (copied into generated files).</summary>
    public string? SourcePath { get; init; }

    /// <summary>Insertion base of the block relative to its lower-left corner (paper mm).</summary>
    public double BaseX { get; init; }

    public double BaseY { get; init; }

    /// <summary>Block drawing units per paper mm (1 when the block is drawn at 1:1 paper size). The frame is inserted at sheet scale ÷ this.</summary>
    public double BlockScale { get; init; } = 1;

    public IReadOnlyDictionary<string, string> AttributeTags { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, TextSlot> TextSlots { get; init; } = new Dictionary<string, TextSlot>();

    /// <summary>Placeholder A3 (420x297): 10 mm border, 70 mm title strip on the right. Replace with the company frame.</summary>
    public static SheetFrame A3Default { get; } = new("HS_FRAME_A3", 420, 297, 15, 15, 320, 267)
    {
        TextSlots = new Dictionary<string, TextSlot>
        {
            ["project"] = new(347, 120, 3.5),
            ["title"] = new(347, 95, 4),
            ["dwg_no"] = new(347, 70, 4),
            ["scale"] = new(347, 50, 3),
            ["date"] = new(347, 35, 3),
        },
    };

    /// <summary>
    /// 건축사사무소 지음 A3 frame: block <c>ZIUM_sheet_architect</c>, drawn 84000×59400 (A3 × 200), base point at the
    /// lower-left corner, no attributes. Inner border 20,10–410,282 with the title column from x 372.5; the fields
    /// are the positions measured on ZIUM sheets (docs/standards/zium_plan_sheet.json in power-cad, sheet mm ÷ 80).
    /// </summary>
    public static SheetFrame ZiumA3 { get; } = new("ZIUM_sheet_architect", 420, 297, 25, 15, 342, 262)
    {
        BlockScale = 200,
        TextSlots = new Dictionary<string, TextSlot>
        {
            ["project"] = new(375, 272, 3),
            ["title"] = new(374, 44.5, 1.9), // the column is 37.5 mm wide; "ERECTION PLAN EL. +4500" fits at 1.9
            ["dwg_no"] = new(387.2, 13.6, 2.5),
            ["scale"] = new(399, 26.3, 1.9),
            ["date"] = new(397.1, 20.8, 1.9),
        },
    };

    public static readonly double[] StandardScales = [1, 2, 2.5, 3, 4, 5, 6, 8, 10, 15, 20, 25, 30, 40, 50, 60, 75, 100, 150, 200, 250, 300, 400, 500];

    /// <summary>Smallest standard scale at which a model of the given size fits in the usable area.</summary>
    public double FitScale(double modelW, double modelH, double fill = 1.0) =>
        StandardScales.FirstOrDefault(s => modelW / s <= AreaW * fill && modelH / s <= AreaH * fill, StandardScales[^1]);

    /// <summary>Smallest standard scale where <paramref name="size"/> mm draws no larger than <paramref name="paper"/> mm.</summary>
    public static double ScaleFor(double size, double paper) =>
        StandardScales.FirstOrDefault(s => size / s <= paper, StandardScales[^1]);
}
