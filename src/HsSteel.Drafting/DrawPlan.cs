using System.Text.Json;
using System.Text.Json.Nodes;

namespace HsSteel.Drafting;

/// <summary>
/// One entity to create. <see cref="Spec"/> uses exactly the JSON shape of power-cad's <c>cad_create</c>
/// (type, layer, start/end, points, center/radius, text/position/height/justify, kind/p1/p2/line_point ...),
/// so a plan can be sent to power-cad unchanged after the merge.
/// </summary>
public sealed record PlanEntity(JsonObject Spec)
{
    public string Type => Spec["type"]!.GetValue<string>();

    public string Layer => Spec["layer"]?.GetValue<string>() ?? "0";
}

/// <summary>A deterministic list of entities plus what they represent. Backend-neutral.</summary>
public sealed class DrawPlan
{
    private readonly List<PlanEntity> _entities = [];

    public string Title { get; init; } = "";

    /// <summary>Drawing scale denominator (10 = 1:10).</summary>
    public double Scale { get; set; } = 1;

    public IReadOnlyList<PlanEntity> Entities => _entities;

    public JsonObject Meta { get; } = [];

    public void Add(JsonObject spec) => _entities.Add(new PlanEntity(spec));

    public static JsonArray Pt(double x, double y) => [Math.Round(x, 4), Math.Round(y, 4)];

    public void Line(string layer, double x1, double y1, double x2, double y2) =>
        Add(new JsonObject { ["type"] = "line", ["layer"] = layer, ["start"] = Pt(x1, y1), ["end"] = Pt(x2, y2) });

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
        Add(new JsonObject { ["type"] = "circle", ["layer"] = layer, ["center"] = Pt(cx, cy), ["radius"] = r });

    public void Arc(string layer, double cx, double cy, double r, double a0, double a1) =>
        Add(new JsonObject { ["type"] = "arc", ["layer"] = layer, ["center"] = Pt(cx, cy), ["radius"] = r, ["start_angle"] = a0, ["end_angle"] = a1 });

    public void Text(string layer, string text, double x, double y, double height, string justify = "middle_center", double rotation = 0) =>
        Add(new JsonObject
        {
            ["type"] = "text", ["layer"] = layer, ["text"] = text, ["position"] = Pt(x, y),
            ["height"] = height, ["justify"] = justify, ["rotation"] = rotation,
        });

    /// <summary>Linear dimension. rotation 0 = horizontal, 90 = vertical. linePoint fixes the dimension line.</summary>
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

    public void Insert(string layer, string block, double x, double y, double scale) =>
        Add(new JsonObject { ["type"] = "insert", ["layer"] = layer, ["name"] = block, ["position"] = Pt(x, y), ["scale"] = scale });

    /// <summary>Axis-aligned extents of everything except inserts and dimensions text.</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Extents()
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        void Take(JsonNode? p)
        {
            if (p is JsonArray a)
            {
                var x = a[0]!.GetValue<double>();
                var y = a[1]!.GetValue<double>();
                (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
            }
        }

        foreach (var e in _entities.Where(e => e.Type != "insert").Select(e => e.Spec))
        {
            foreach (var k in new[] { "start", "end", "position", "p1", "p2", "line_point" })
            {
                Take(e[k]);
            }

            if (e["points"] is JsonArray pts)
            {
                foreach (var p in pts)
                {
                    Take(p);
                }
            }

            if (e["center"] is JsonArray c && e["radius"] is JsonNode r)
            {
                var rr = r.GetValue<double>();
                Take(Pt(c[0]!.GetValue<double>() - rr, c[1]!.GetValue<double>() - rr));
                Take(Pt(c[0]!.GetValue<double>() + rr, c[1]!.GetValue<double>() + rr));
            }
        }

        return (x0, y0, x1, y1);
    }

    public JsonObject ToJson() => new()
    {
        ["title"] = Title,
        ["scale"] = Scale,
        ["meta"] = Meta.DeepClone(),
        ["entities"] = new JsonArray([.. _entities.Select(e => (JsonNode)e.Spec.DeepClone())]),
    };

    public string ToJsonString() => ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
