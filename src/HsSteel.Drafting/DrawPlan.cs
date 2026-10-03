using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HsSteel.Drafting;

/// <summary>
/// One entity to create. <see cref="Spec"/> has exactly the JSON shape of power-cad's <c>cad_create</c>
/// (type, layer, start/end, points, center/radius, text/position/height/justify, kind/p1/p2/line_point, name/scale ...),
/// so it can be sent to power-cad unchanged. <see cref="Tag"/> carries HS-STEEL data (mark, part, assembly) that
/// backends store as XData under the "HS-STEEL" application; it is never part of the create spec.
/// </summary>
public sealed record PlanEntity(JsonObject Spec, JsonObject? Tag = null)
{
    public string Type => Spec["type"]!.GetValue<string>();

    public string Layer => Spec["layer"]?.GetValue<string>() ?? "0";
}

/// <summary>A deterministic, backend-neutral list of entities in model units (mm).</summary>
public sealed class DrawPlan
{
    private static readonly string[] PointKeys = ["start", "end", "position", "p1", "p2", "line_point", "center"];
    private readonly List<PlanEntity> _entities = [];

    public string Title { get; set; } = "";

    /// <summary>Drawing scale denominator (10 = 1:10). Text heights and paper offsets are already multiplied by it.</summary>
    public double Scale { get; set; } = 1;

    public IReadOnlyList<PlanEntity> Entities => _entities;

    public JsonObject Meta { get; } = [];

    /// <summary>Current tag applied to entities added from now on (null = none).</summary>
    public JsonObject? CurrentTag { get; set; }

    public void Add(JsonObject spec) => _entities.Add(new PlanEntity(spec, CurrentTag?.DeepClone().AsObject()));

    public void AddRaw(PlanEntity e) => _entities.Add(e);

    public static JsonArray Pt(double x, double y) => [Math.Round(x, 3), Math.Round(y, 3)];

    public void Line(string layer, double x1, double y1, double x2, double y2)
    {
        if (Math.Abs(x1 - x2) + Math.Abs(y1 - y2) > 1e-6)
        {
            Add(new JsonObject { ["type"] = "line", ["layer"] = layer, ["start"] = Pt(x1, y1), ["end"] = Pt(x2, y2) });
        }
    }

    public void Rect(string layer, double x, double y, double w, double h) =>
        Polyline(layer, true, (x, y), (x + w, y), (x + w, y + h), (x, y + h));

    public void Polyline(string layer, bool closed, params (double X, double Y)[] pts)
    {
        var arr = new JsonArray();
        foreach (var (px, py) in pts)
        {
            arr.Add(Pt(px, py));
        }

        Add(new JsonObject { ["type"] = "polyline", ["layer"] = layer, ["points"] = arr, ["closed"] = closed });
    }

    public void Circle(string layer, double cx, double cy, double r) =>
        Add(new JsonObject { ["type"] = "circle", ["layer"] = layer, ["center"] = Pt(cx, cy), ["radius"] = Math.Round(r, 3) });

    public void Arc(string layer, double cx, double cy, double r, double a0, double a1) =>
        Add(new JsonObject { ["type"] = "arc", ["layer"] = layer, ["center"] = Pt(cx, cy), ["radius"] = Math.Round(r, 3), ["start_angle"] = a0, ["end_angle"] = a1 });

    public void Text(string layer, string text, double x, double y, double height, string justify = "middle_center", double rotation = 0) =>
        Add(new JsonObject
        {
            ["type"] = "text", ["layer"] = layer, ["text"] = text, ["position"] = Pt(x, y),
            ["height"] = Math.Round(height, 3), ["justify"] = justify, ["rotation"] = rotation,
        });

    /// <summary>Linear dimension. rotation 0 = horizontal, 90 = vertical; linePoint fixes the dimension line.</summary>
    public void Dim(string layer, (double X, double Y) p1, (double X, double Y) p2, (double X, double Y) linePoint, double rotation, string? text = null)
    {
        var o = new JsonObject
        {
            ["type"] = "dimension", ["layer"] = layer, ["kind"] = "rotated",
            ["p1"] = Pt(p1.X, p1.Y), ["p2"] = Pt(p2.X, p2.Y), ["line_point"] = Pt(linePoint.X, linePoint.Y), ["rotation"] = rotation,
        };
        if (text is not null)
        {
            o["text"] = text;
        }

        Add(o);
    }

    /// <summary>Leader from the arrow point through the given points (power-cad "leader").</summary>
    public void Leader(string layer, params (double X, double Y)[] pts)
    {
        var arr = new JsonArray();
        foreach (var (px, py) in pts)
        {
            arr.Add(Pt(px, py));
        }

        Add(new JsonObject { ["type"] = "leader", ["layer"] = layer, ["points"] = arr });
    }

    public void Insert(string layer, string block, double x, double y, double scale, JsonObject? attributes = null)
    {
        var o = new JsonObject { ["type"] = "insert", ["layer"] = layer, ["name"] = block, ["position"] = Pt(x, y), ["scale"] = scale };
        if (attributes is not null)
        {
            o["attributes"] = attributes;
        }

        Add(o);
    }

    /// <summary>Copies all entities of <paramref name="other"/> translated by (dx, dy).</summary>
    public void Append(DrawPlan other, double dx, double dy)
    {
        foreach (var e in other.Entities)
        {
            var s = e.Spec.DeepClone().AsObject();
            Translate(s, dx, dy);
            _entities.Add(new PlanEntity(s, e.Tag?.DeepClone().AsObject()));
        }
    }

    private static void Translate(JsonObject s, double dx, double dy)
    {
        static JsonArray Move(JsonNode p, double dx, double dy) => Pt(p[0]!.GetValue<double>() + dx, p[1]!.GetValue<double>() + dy);
        foreach (var k in PointKeys)
        {
            if (s[k] is JsonArray a)
            {
                s[k] = Move(a, dx, dy);
            }
        }

        if (s["points"] is JsonArray pts)
        {
            var moved = new JsonArray();
            foreach (var p in pts)
            {
                moved.Add(Move(p!, dx, dy));
            }

            s["points"] = moved;
        }
    }

    /// <summary>Extents of all entities except inserts (frames). Text is approximated by its insertion box.</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Extents() => Extents(_ => true);

    /// <summary>Extents of the entities accepted by <paramref name="filter"/> (inserts are always excluded).</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Extents(Func<PlanEntity, bool> filter)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        void Take(double x, double y) => (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        foreach (var e in _entities.Where(e => e.Type != "insert" && filter(e)))
        {
            var s = e.Spec;
            foreach (var k in PointKeys)
            {
                if (s[k] is JsonArray a)
                {
                    double x = a[0]!.GetValue<double>(), y = a[1]!.GetValue<double>();
                    if (k == "center" && s["radius"] is JsonNode r)
                    {
                        var rr = r.GetValue<double>();
                        Take(x - rr, y - rr);
                        Take(x + rr, y + rr);
                    }
                    else
                    {
                        Take(x, y);
                    }
                }
            }

            if (s["points"] is JsonArray pts)
            {
                foreach (var p in pts)
                {
                    Take(p![0]!.GetValue<double>(), p[1]!.GetValue<double>());
                }
            }

            if (e.Type == "text")
            {
                var (tx, ty, tw, th) = TextBox(s);
                Take(tx, ty);
                Take(tx + tw, ty + th);
            }
        }

        return x0 == double.MaxValue ? (0, 0, 0, 0) : (x0, y0, x1, y1);
    }

    /// <summary>Approximate text box (left, bottom, width, height) for layout checks; width = 0.8 h per character.</summary>
    public static (double X, double Y, double W, double H) TextBox(JsonObject s)
    {
        var h = s["height"]!.GetValue<double>();
        var text = s["text"]!.GetValue<string>();
        var w = TextWidth(text, h);
        var p = s["position"]!.AsArray();
        double x = p[0]!.GetValue<double>(), y = p[1]!.GetValue<double>();
        var rot = s["rotation"]?.GetValue<double>() ?? 0;
        var j = s["justify"]?.GetValue<string>() ?? "left";
        double ox = j.EndsWith("center", StringComparison.Ordinal) ? -w / 2 : j.EndsWith("right", StringComparison.Ordinal) ? -w : 0;
        double oy = j.StartsWith("middle", StringComparison.Ordinal) ? -h / 2 : j.StartsWith("top", StringComparison.Ordinal) ? -h : 0;
        return Math.Abs(rot - 90) < 1e-6 ? (x - h - oy, y + ox, h, w) : (x + ox, y + oy, w, h);
    }

    /// <summary>Text width estimate: Korean characters are full width.</summary>
    public static double TextWidth(string text, double h) => text.Sum(c => c > 0x2E80 ? 1.0 : 0.75) * h;

    public JsonObject ToJson() => new()
    {
        ["title"] = Title,
        ["scale"] = Scale,
        ["meta"] = Meta.DeepClone(),
        ["entities"] = new JsonArray([.. _entities.Select(e =>
        {
            var o = e.Spec.DeepClone().AsObject();
            if (e.Tag is not null)
            {
                o["hs"] = e.Tag.DeepClone();
            }

            return (JsonNode)o;
        })]),
    };

    public string ToJsonString() => ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// Versioned, non-authorizing handoff for Power CAD. Entity create specs are kept
    /// separate from HS-STEEL tags because Power CAD CreateSpec rejects unknown fields.
    /// Tags remain evidence for later XData persistence and must never be silently dropped.
    /// </summary>
    public JsonObject ToPowerCadHandoff()
    {
        var entities = new JsonArray();
        foreach (var entity in _entities)
        {
            entities.Add(new JsonObject
            {
                ["spec"] = entity.Spec.DeepClone(),
                ["tag"] = entity.Tag?.DeepClone(),
            });
        }

        var payload = new JsonObject
        {
            ["schema"] = "hs-steel-draw-plan/1",
            ["producer"] = "khs0927/hs-steel-cad",
            ["title"] = Title,
            ["units"] = "mm",
            ["scale"] = Scale,
            ["meta"] = Meta.DeepClone(),
            ["entities"] = entities,
            ["execution_authorized"] = false,
            ["may_execute_mutation"] = false,
            ["requires_live_document_binding"] = true,
            ["tags_require_xdata_persistence"] = true,
        };
        var bytes = Encoding.UTF8.GetBytes(
            payload.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        payload["contract_digest"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return payload;
    }

    public static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
