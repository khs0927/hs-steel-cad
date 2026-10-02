using System.Text.Json.Nodes;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;

namespace HsSteel.Drafting;

/// <summary>
/// CAD-free backend: writes draw plans to DXF or DWG (by extension) with ACadSharp.
/// - Dimensions are written exploded (extension lines, dimension line, ticks, text) so files look the same
///   everywhere; dimension texts are nudged to avoid overlapping each other. The AutoCAD backend (power-cad)
///   creates real DIMENSION entities instead.
/// - Entity tags are stored as XData under the "HS-STEEL" application (key=value strings).
/// - The frame block is copied from <see cref="SheetFrame.SourcePath"/> when given (the company 도곽), otherwise a
///   placeholder frame block is defined from the frame size.
/// </summary>
public static class DxfExporter
{
    public const string AppName = "HS-STEEL";

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

        var app = new AppId(AppName);
        doc.AppIds.Add(app);
        foreach (var plan in plans)
        {
            var ctx = new Context(doc, frame, app, plan.Scale);
            foreach (var e in plan.Entities)
            {
                foreach (var entity in ctx.Convert(e.Spec))
                {
                    entity.Layer = doc.Layers[e.Layer];
                    if (e.Tag is not null)
                    {
                        var records = e.Tag.Select(kv => ExtendedDataRecord.Create(GroupCodeValueType.String, $"{kv.Key}={kv.Value}"));
                        entity.ExtendedData.Add(app, records);
                    }

                    doc.Entities.Add(entity);
                }
            }
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.Create(path);
            using var writer = new DwgWriter(stream, doc);
            writer.Write();
        }
        else
        {
            using var stream = File.Create(path);
            using var writer = new DxfWriter(stream, doc, false);
            writer.Write();
        }
    }

    public static void Write(IEnumerable<Sheet> sheets, SheetFrame frame, string path) => Write(sheets.Select(s => s.Plan), frame, path);

    private sealed class Context(CadDocument doc, SheetFrame frame, AppId app, double scale)
    {
        private readonly List<(double X0, double Y0, double X1, double Y1)> _dimTexts = [];

        private double Tick => 1.0 * scale;

        private double DimText => 2.2 * scale;

        public IEnumerable<Entity> Convert(JsonObject s)
        {
            _ = app;
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
                case "leader":
                    foreach (var x in Leader(s))
                    {
                        yield return x;
                    }

                    break;
                case "insert":
                    yield return Insert(s);
                    break;
                case "dimension":
                    foreach (var x in ExplodedDimension(s))
                    {
                        yield return x;
                    }

                    break;
            }
        }

        private Insert Insert(JsonObject s)
        {
            var block = EnsureFrameBlock(s["name"]!.GetValue<string>());
            var k = D(s["scale"], 1);
            var ins = new Insert(block) { InsertPoint = P(s["position"]), XScale = k, YScale = k, ZScale = k };
            if (s["attributes"] is JsonObject attrs && block.AttributeDefinitions.Any())
            {
                ins.UpdateAttributes();
                foreach (var a in ins.Attributes)
                {
                    if (attrs[a.Tag] is JsonNode v)
                    {
                        a.Value = v.GetValue<string>();
                    }
                }
            }

            return ins;
        }

        private IEnumerable<Entity> Leader(JsonObject s)
        {
            var pts = s["points"]!.AsArray().Select(P).ToList();
            for (var i = 0; i + 1 < pts.Count; i++)
            {
                yield return new Line { StartPoint = pts[i], EndPoint = pts[i + 1] };
            }

            if (pts.Count >= 2)
            {
                var a = pts[0];
                var b = pts[1];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var len = Math.Sqrt((dx * dx) + (dy * dy));
                if (len > 1e-9)
                {
                    var (ux, uy) = (dx / len, dy / len);
                    var size = 2.5 * scale;
                    var back = new XYZ(a.X + (ux * size), a.Y + (uy * size), 0);
                    var w = size * 0.3;
                    yield return new Solid
                    {
                        FirstCorner = a,
                        SecondCorner = new XYZ(back.X - (uy * w), back.Y + (ux * w), 0),
                        ThirdCorner = new XYZ(back.X + (uy * w), back.Y - (ux * w), 0),
                        FourthCorner = new XYZ(back.X + (uy * w), back.Y - (ux * w), 0),
                    };
                }
            }
        }

        private IEnumerable<Entity> ExplodedDimension(JsonObject s)
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

            var ext = 1.2 * scale;
            yield return ExtLine(p1, a, ext);
            yield return ExtLine(p2, b, ext);
            yield return new Line { StartPoint = a, EndPoint = b };
            foreach (var c in new[] { a, b })
            {
                yield return new Line { StartPoint = new XYZ(c.X - Tick, c.Y - Tick, 0), EndPoint = new XYZ(c.X + Tick, c.Y + Tick, 0) };
            }

            var span = vertical ? Math.Abs(p2.Y - p1.Y) : Math.Abs(p2.X - p1.X);
            var label = s["text"]?.GetValue<string>() ?? $"{span:0.#}";
            var h = DimText;
            var w = DrawPlan.TextWidth(label, h);
            var mid = vertical ? (a.Y + b.Y) / 2 : (a.X + b.X) / 2;
            var off = h * 0.4;
            // Nudge outwards until the text does not overlap a previous dimension text.
            for (var tries = 0; tries < 6; tries++)
            {
                var box = vertical
                    ? (a.X - off - h, mid - (w / 2), a.X - off, mid + (w / 2))
                    : (mid - (w / 2), a.Y + off, mid + (w / 2), a.Y + off + h);
                if (!_dimTexts.Any(t => t.X0 < box.Item3 && box.Item1 < t.X1 && t.Y0 < box.Item4 && box.Item2 < t.Y1))
                {
                    _dimTexts.Add(box);
                    break;
                }

                if (vertical)
                {
                    mid += w * 0.6 * (tries % 2 == 0 ? 1 : -1) * (tries + 1);
                }
                else
                {
                    off += h * 1.1;
                }
            }

            var at = vertical ? new XYZ(a.X - off, mid, 0) : new XYZ(mid, a.Y + off, 0);
            yield return Text(label, at, h, vertical ? 90 : 0, "bottom_center");
        }

        private static Line ExtLine(XYZ from, XYZ to, double beyond)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len < 1e-9)
            {
                return new Line { StartPoint = from, EndPoint = to };
            }

            var gap = Math.Min(len * 0.2, beyond);
            return new Line
            {
                StartPoint = new XYZ(from.X + (dx / len * gap), from.Y + (dy / len * gap), 0),
                EndPoint = new XYZ(to.X + (dx / len * beyond), to.Y + (dy / len * beyond), 0),
            };
        }

        private BlockRecord EnsureFrameBlock(string name)
        {
            if (doc.BlockRecords.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var rec = new BlockRecord(name);
            if (frame.SourcePath is not null && File.Exists(frame.SourcePath) && name == frame.BlockName)
            {
                foreach (var e in FrameSource.Entities(frame))
                {
                    rec.Entities.Add(e);
                }
            }
            else
            {
                rec.Entities.Add(Rect(0, 0, frame.Width, frame.Height));
                rec.Entities.Add(Rect(10, 10, frame.Width - 20, frame.Height - 20));
                rec.Entities.Add(Rect(frame.AreaX, frame.AreaY, frame.AreaW, frame.AreaH));
                var tx = frame.AreaX + frame.AreaW + 5;
                rec.Entities.Add(new Line { StartPoint = new XYZ(tx, 10, 0), EndPoint = new XYZ(tx, frame.Height - 10, 0) });
                rec.Entities.Add(new TextEntity
                {
                    Value = "PLACEHOLDER FRAME - REPLACE WITH COMPANY TITLE BLOCK",
                    InsertPoint = new XYZ(tx + 5, frame.Height - 20, 0),
                    Height = 2.5,
                });
            }

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

    internal static TextEntity Text(string value, XYZ at, double height, double rotationDeg, string? justify)
    {
        var t = new TextEntity { Value = value, InsertPoint = at, Height = height, Rotation = rotationDeg * Math.PI / 180, WidthFactor = 0.8 };
        var parts = (justify ?? "left").Split('_');
        var v = parts.Length == 2 ? parts[0] : "baseline";
        var h = parts.Length == 2 ? parts[1] : parts[0];
        var hor = h switch { "center" => TextHorizontalAlignment.Center, "right" => TextHorizontalAlignment.Right, _ => TextHorizontalAlignment.Left };
        var ver = v switch
        {
            "middle" => TextVerticalAlignmentType.Middle,
            "top" => TextVerticalAlignmentType.Top,
            "bottom" => TextVerticalAlignmentType.Bottom,
            _ => TextVerticalAlignmentType.Baseline,
        };
        if (hor != TextHorizontalAlignment.Left || ver != TextVerticalAlignmentType.Baseline)
        {
            t.HorizontalAlignment = hor;
            t.VerticalAlignment = ver;
            t.AlignmentPoint = at;
        }

        return t;
    }

    private static XYZ P(JsonNode? n) => new(n![0]!.GetValue<double>(), n[1]!.GetValue<double>(), 0);

    private static double D(JsonNode? n, double fallback = 0) => n?.GetValue<double>() ?? fallback;
}

/// <summary>Reads the company frame from a DWG/DXF: either a named block, or the whole model space.</summary>
public static class FrameSource
{
    public static CadDocument Read(string path) =>
        path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) ? DxfReader.Read(path) : DwgReader.Read(path);

    /// <summary>Entities of the frame (cloned), relative to the frame's insertion base.</summary>
    public static IEnumerable<Entity> Entities(SheetFrame frame)
    {
        var doc = Read(frame.SourcePath!);
        IEnumerable<Entity> source = doc.BlockRecords.TryGetValue(frame.BlockName, out var rec) ? rec.Entities : doc.Entities;
        foreach (var e in source)
        {
            if (e.Clone() is Entity c)
            {
                c.Layer = new Layer(Layers.Frame);
                yield return c;
            }
        }
    }

    /// <summary>
    /// Builds a <see cref="SheetFrame"/> from a company frame file. Size comes from the block (or model space)
    /// extents; the drawing area is the largest closed rectangle inside it that is not the border itself,
    /// unless given explicitly. Attribute tags present in the block are mapped to fields by name.
    /// </summary>
    public static SheetFrame FromFile(string path, string? blockName = null, (double X, double Y, double W, double H)? area = null)
    {
        var doc = Read(path);
        BlockRecord? rec = null;
        if (blockName is not null && !doc.BlockRecords.TryGetValue(blockName, out rec))
        {
            throw new KeyNotFoundException($"Block '{blockName}' not found in {path}.");
        }

        var ents = (rec?.Entities ?? doc.Entities).ToList();
        var boxes = ents.Select(e => e.GetBoundingBox()).Where(b => b.Max.X > b.Min.X || b.Max.Y > b.Min.Y).ToList();
        double x0 = boxes.Min(b => b.Min.X), y0 = boxes.Min(b => b.Min.Y), x1 = boxes.Max(b => b.Max.X), y1 = boxes.Max(b => b.Max.Y);
        double w = x1 - x0, h = y1 - y0;
        var bx = rec?.BlockEntity?.BasePoint.X ?? 0;
        var by = rec?.BlockEntity?.BasePoint.Y ?? 0;

        (double X, double Y, double W, double H) a;
        if (area is { } given)
        {
            a = given;
        }
        else
        {
            var rects = ents.OfType<LwPolyline>().Where(p => p.IsClosed && p.Vertices.Count == 4).Select(p =>
            {
                var xs = p.Vertices.Select(v => v.Location.X).ToList();
                var ys = p.Vertices.Select(v => v.Location.Y).ToList();
                return (X: xs.Min() - x0, Y: ys.Min() - y0, W: xs.Max() - xs.Min(), H: ys.Max() - ys.Min());
            }).Where(r => r.W < w * 0.97 || r.H < h * 0.97).OrderByDescending(r => r.W * r.H).ToList();
            a = rects.Count > 0 && rects[0].W * rects[0].H > w * h * 0.4 ? rects[0] : (w * 0.04, h * 0.05, w * 0.72, h * 0.9);
            a = (a.X + 3, a.Y + 3, a.W - 6, a.H - 6);
        }

        var tags = new Dictionary<string, string>();
        if (rec is not null)
        {
            foreach (var def in rec.AttributeDefinitions)
            {
                var t = def.Tag.ToUpperInvariant();
                var field = t switch
                {
                    _ when t.Contains("PROJ") || t.Contains("공사") => "project",
                    _ when t.Contains("NO") || t.Contains("번호") => "dwg_no",
                    _ when t.Contains("SCALE") || t.Contains("축척") => "scale",
                    _ when t.Contains("DATE") || t.Contains("일자") || t.Contains("날짜") => "date",
                    _ when t.Contains("TITLE") || t.Contains("명") => "title",
                    _ => null,
                };
                if (field is not null)
                {
                    tags.TryAdd(field, def.Tag);
                }
            }
        }

        return new SheetFrame(blockName ?? Path.GetFileNameWithoutExtension(path), w, h, a.X, a.Y, a.W, a.H)
        {
            SourcePath = path,
            BaseX = bx - x0,
            BaseY = by - y0,
            AttributeTags = tags,
            TextSlots = tags.Count > 0 ? new Dictionary<string, TextSlot>() : new Dictionary<string, TextSlot>
            {
                ["title"] = new(a.X + a.W + 5, a.Y + (a.H * 0.3), 3.5),
                ["dwg_no"] = new(a.X + a.W + 5, a.Y + (a.H * 0.2), 3.5),
                ["scale"] = new(a.X + a.W + 5, a.Y + (a.H * 0.1), 3),
            },
        };
    }
}
