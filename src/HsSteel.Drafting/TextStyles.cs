using System.Text.Json.Nodes;

namespace HsSteel.Drafting;

/// <summary>
/// Text/dimension style standard for generated drawings (COLLAB #4: Korean text drew as bars with a Latin-only font).
/// The HS-STEEL legacy template's own Standard/GHS styles use <c>txt.shx</c> + big font <c>whgtxt.shx</c> (Hangul), so
/// HS-KOR reuses exactly that pair. A drawing must contain a text style of this name (and a dimension style of the same
/// name that uses it) before the plan is created; the power-cad payload carries both definitions.
/// </summary>
public static class TextStyles
{
    public const string Korean = "HS-KOR";

    /// <summary>Font file (SHX) of the Korean text style, taken from the template's Standard style.</summary>
    public const string Font = "txt.shx";

    /// <summary>Hangul big font (SHX) of the Korean text style, taken from the template's Standard style.</summary>
    public const string BigFont = "whgtxt.shx";

    /// <summary>Text style definitions to create in the target drawing when missing.</summary>
    public static JsonArray TextStyleDefs() =>
        [new JsonObject { ["name"] = Korean, ["font"] = Font, ["big_font"] = BigFont, ["width_factor"] = 1.0 }];

    /// <summary>Dimension style definitions (based on Standard, text style HS-KOR).</summary>
    public static JsonArray DimStyleDefs() =>
        [new JsonObject { ["name"] = Korean, ["based_on"] = "Standard", ["text_style"] = Korean }];
}
