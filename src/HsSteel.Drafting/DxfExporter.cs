using System.Text.Json.Nodes;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;

namespace HsSteel.Drafting;

/// <summary>
/// CAD-free backend: writes a <see cref="DrawPlan"/> to DXF with ACadSharp.
/// Dimensions are written exploded (extension lines + dimension line + ticks + text) so the file
/// opens identically everywhere; the AutoCAD backend (power-cad) creates real DIMENSION entities instead.
/// If the frame block is not available, a placeholder frame block is defined from <see cref="SheetFrame"/>.
/// </summary>
public static class DxfExporter
{
    public static void Write(IEnumerable<DrawPlan> plans, SheetFrame frame, string path)
    {
        var doc = new CadDocument();
        foreach (var (name, color, _) in Layers.All)
        {
            if (!doc.Layers.Contains(name))
            {
                doc.Layers.Add(new Layer(name) { Color = new Color(color) });
            }
        }

        foreach (var plan in plans)
        {
            var tick = 1.2 * plan.Scale;
            foreach (var e in plan.Entities)
            {
                foreach (var entity in Convert(doc, e.Spec, frame, tick, 2.5 * plan.Scale))
                {
                    entity.Layer = doc.Layers[e.Layer];
                    doc.Entities.Add(entity);
                }
            }
        }

        using var stream = File.Create(path);
        using var writer = new DxfWriter(stream, doc, false);
        writer.Write();
    }

    private static XYZ P(JsonNode? n) => new(n![0]!.GetValue<double>(), n[1]!.GetValue<double>(), 0);

    private static double D(JsonNode? n, double fallback = 0) => n?.GetValue<double>() ?? fallback;

    private static IEnumerable<Entity> Convert(CadDocument doc, JsonObject s, SheetFrame frame, double tick, double textH)
    {
        switch (s["type"]!.GetValue<string>())
        {
            case "line":
                yield return new Line { StartPoint = P(s["start"]), EndPoint = P(s["end"]) };
                break;
            case "polyline":
                var pl = new LwPolyline { IsClosed = s["closed"]?.GetValue<bool>() ?? false };
                foreach (var p in s["points"]!.AsArray())
                {
                    var v = P(p);
                    pl.Vertices.Add(new LwPolyline.Vertex(new XY(v.X, v.Y)));
                }

                yield return pl;
                break;
            case "circle":
                yield return new Circle { Center = P(s["center"]), Radius = D(s["radius"]) };
                break;
            case "arc":
                yield return new Arc
                {
                    Center = P(s["center"]), Radius = D(s["radius"]),
                    StartAngle = D(s["start_angle"]) * Math.PI / 180, EndAngle = D(s["end_angle"]) * Math.PI / 180,
                };
                break;
            case "text":
                yield return Text(s["text"]!.GetValue<string>(), P(s["position"]), D(s["height"]), D(s["rotation"]), s["justify"]?.GetValue<string>());
                break;
            case "insert":
                var block = EnsureFrameBlock(doc, s["name"]!.GetValue<string>(), frame);
                var k = D(s["scale"], 1);
                yield return new Insert(block) { InsertPoint = P(s["position"]), XScale = k, YScale = k, ZScale = k };
                break;
            case "dimension":
                foreach (var x in ExplodedDimension(s, tick, textH))
                {
                    yield return x;
                }

                break;
        }
    }

    private static TextEntity Text(string value, XYZ at, double height, double rotationDeg, string? justify)
    {
        var t = new TextEntity { Value = value, InsertPoint = at, Height = height, Rotation = rotationDeg * Math.PI / 180 };
        var (h, v) = justify switch
        {
            "middle_center" => (TextHorizontalAlignment.Center, TextVerticalAlignmentType.Middle),
            "middle_left" => (TextHorizontalAlignment.Left, TextVerticalAlignmentType.Middle),
            "bottom_center" => (TextHorizontalAlignment.Center, TextVerticalAlignmentType.Bottom),
            _ => (TextHorizontalAlignment.Left, TextVerticalAlignmentType.Baseline),
        };
        if (h != TextHorizontalAlignment.Left || v != TextVerticalAlignmentType.Baseline)
        {
            t.HorizontalAlignment = h;
            t.VerticalAlignment = v;
            t.AlignmentPoint = at;
        }

        return t;
    }

    private static IEnumerable<Entity> ExplodedDimension(JsonObject s, double tick, double textH)
    {
        var p1 = P(s["p1"]);
        var p2 = P(s["p2"]);
        var lp = P(s["line_point"]);
        var vertical = Math.Abs(D(s["rotation"]) - 90) < 1e-6;
        XYZ a, b;
        if (vertical)
        {
            a = new XYZ(lp.X, p1.Y, 0);
            b = new XYZ(lp.X, p2.Y, 0);
        }
        else
        {
            a = new XYZ(p1.X, lp.Y, 0);
            b = new XYZ(p2.X, lp.Y, 0);
        }

        yield return new Line { StartPoint = p1, EndPoint = a };
        yield return new Line { StartPoint = p2, EndPoint = b };
        yield return new Line { StartPoint = a, EndPoint = b };
        foreach (var c in new[] { a, b })
        {
            yield return new Line { StartPoint = new XYZ(c.X - tick, c.Y - tick, 0), EndPoint = new XYZ(c.X + tick, c.Y + tick, 0) };
        }

        var label = s["text"]?.GetValue<string>() ?? $"{(vertical ? Math.Abs(p2.Y - p1.Y) : Math.Abs(p2.X - p1.X)):0.#}";
        var mid = new XYZ((a.X + b.X) / 2, (a.Y + b.Y) / 2, 0);
        var at = vertical ? new XYZ(mid.X - (textH * 0.6), mid.Y, 0) : new XYZ(mid.X, mid.Y + (textH * 0.6), 0);
        yield return Text(label, at, textH, vertical ? 90 : 0, "bottom_center");
    }

    private static BlockRecord EnsureFrameBlock(CadDocument doc, string name, SheetFrame f)
    {
        if (doc.BlockRecords.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var rec = new BlockRecord(name);
        rec.Entities.Add(Rect(0, 0, f.Width, f.Height));
        rec.Entities.Add(Rect(f.AreaX, f.AreaY, f.AreaW, f.AreaH));
        rec.Entities.Add(new TextEntity
        {
            Value = "PLACEHOLDER FRAME - replace with company 도곽",
            InsertPoint = new XYZ(f.AreaX + f.AreaW + 5, f.AreaY + 5, 0),
            Height = 2.5,
            Rotation = Math.PI / 2,
        });
        doc.BlockRecords.Add(rec);
        return rec;
    }

    private static LwPolyline Rect(double x, double y, double w, double h)
    {
        var r = new LwPolyline { IsClosed = true };
        foreach (var (px, py) in new[] { (x, y), (x + w, y), (x + w, y + h), (x, y + h) })
        {
            r.Vertices.Add(new LwPolyline.Vertex(new XY(px, py)));
        }

        return r;
    }
}
