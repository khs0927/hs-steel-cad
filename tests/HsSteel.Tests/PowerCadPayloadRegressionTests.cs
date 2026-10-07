using System.Text.Json.Nodes;
using HsSteel.Drafting;
using HsSteel.Mcp;

namespace HsSteel.Tests;

/// <summary>
/// Payload-level regressions for hs_drawings_to_powercad (COLLAB #1/#3/#4): everything the live AutoCAD run rejected or
/// drew wrongly must be impossible in the payload itself.
/// </summary>
public sealed class PowerCadPayloadRegressionTests(AssetDbFixture fx) : IClassFixture<AssetDbFixture>
{
    /// <summary>Leader arrow size (paper mm, ISO-25 / template dimension style); a leader must be at least this long on paper.</summary>
    private const double ArrowSizePaper = 2.5;

    private static double Len(JsonNode a, JsonNode b) =>
        Math.Sqrt(Math.Pow(a[0]!.GetValue<double>() - b[0]!.GetValue<double>(), 2) + Math.Pow(a[1]!.GetValue<double>() - b[1]!.GetValue<double>(), 2));

    [Fact]
    public void Demo_payload_has_no_empty_text_short_leader_or_undefined_layer_and_style()
    {
        if (!Fx.HasLegacy)
        {
            return;
        }

        var ws = Fx.Ws.Value;
        ws.Save(Demo.Sample());
        var r = JsonNode.Parse(new AssetTools(ws, fx.Options).DrawingsToPowerCad("SAMPLE-FRAME"))!;
        var scales = r["sheets"]!.AsArray().ToDictionary(s => s!["dwg_no"]!.GetValue<string>(), s => s!["scale"]!.GetValue<double>());
        var payloads = r["payloads"]!.AsArray();
        var layers = payloads[0]!["layers"]!.AsArray().Select(l => l!["name"]!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var textStyles = payloads[0]!["text_styles"]!.AsArray().Select(l => l!["name"]!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dimStyles = payloads[0]!["dim_styles"]!.AsArray().Select(l => l!["name"]!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(TextStyles.Korean, textStyles);
        Assert.Contains(TextStyles.Korean, dimStyles);
        var def = payloads[0]!["text_styles"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == TextStyles.Korean)!;
        Assert.Equal("whgtxt.shx", def["big_font"]!.GetValue<string>());

        var entities = payloads.SelectMany(p => p!["entities"]!.AsArray()).Select(e => e!.AsObject()).ToList();
        Assert.NotEmpty(entities);
        var texts = 0;
        var leaders = 0;
        foreach (var e in entities)
        {
            var type = e["type"]!.GetValue<string>();
            Assert.Contains(e["layer"]?.GetValue<string>() ?? "0", layers.Append("0"));
            if (type is "text" or "mtext")
            {
                texts++;
                Assert.False(string.IsNullOrWhiteSpace(e["text"]?.GetValue<string>()), "empty text in payload");
                Assert.Contains(e["style"]?.GetValue<string>() ?? "", textStyles);
            }
            else if (type == "dimension")
            {
                Assert.Contains(e["style"]?.GetValue<string>() ?? "", dimStyles);
            }
            else if (type == "leader")
            {
                leaders++;
                var pts = e["points"]!.AsArray();
                var scale = scales[e["hs"]!["sheet"]!.GetValue<string>()];
                var min = ArrowSizePaper * Math.Max(scale, 1);
                for (var i = 1; i < pts.Count; i++)
                {
                    Assert.True(Len(pts[i - 1]!, pts[i]!) >= min - 1e-6, $"leader segment {Len(pts[i - 1]!, pts[i]!):0.##} < {min} (arrow x scale)");
                }
            }
        }

        Assert.True(texts > 0);
        Assert.True(leaders > 0, "demo project should produce weld/callout leaders");
    }
}
