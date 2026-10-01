namespace HsSteel.Drafting;

/// <summary>
/// The company sheet border (도곽). It is the user's own block; we only need its size at 1:1 and the
/// rectangle inside it where views may be placed (excluding title block / revision / notes areas).
/// Everything in the drawing is modelled 1:1 in mm and the frame is inserted scaled by the drawing scale.
/// </summary>
/// <param name="BlockName">Block name of the frame in the target drawing (e.g. the company A3 frame).</param>
/// <param name="Width">Frame width at 1:1 (paper mm).</param>
/// <param name="Height">Frame height at 1:1.</param>
/// <param name="AreaX">Left of the usable drawing area, relative to the frame insertion point.</param>
/// <param name="AreaY">Bottom of the usable area.</param>
/// <param name="AreaW">Usable width.</param>
/// <param name="AreaH">Usable height.</param>
public sealed record SheetFrame(string BlockName, double Width, double Height, double AreaX, double AreaY, double AreaW, double AreaH)
{
    /// <summary>Placeholder A3 (420x297) with a 10 mm margin and a 60 mm title strip on the right. Replace with the company frame.</summary>
    public static SheetFrame A3Default { get; } = new("HS_FRAME_A3", 420, 297, 10, 10, 340, 277);

    public static readonly double[] StandardScales = [1, 2, 2.5, 5, 10, 15, 20, 25, 30, 40, 50, 60, 75, 100];

    /// <summary>Smallest standard scale at which a model of the given size fits in the usable area.</summary>
    public double FitScale(double modelW, double modelH)
    {
        foreach (var s in StandardScales)
        {
            if (modelW / s <= AreaW && modelH / s <= AreaH)
            {
                return s;
            }
        }

        return Math.Ceiling(Math.Max(modelW / AreaW, modelH / AreaH));
    }
}
