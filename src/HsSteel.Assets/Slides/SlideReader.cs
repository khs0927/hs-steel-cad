using System.Globalization;
using System.Text;

namespace HsSteel.Assets.Slides;

/// <summary>One drawable of a slide, in file order: a vector (2 points) or a solid fill (3-4 points) in a given ACI color.</summary>
public sealed record SlideShape(bool IsSolid, int Color, IReadOnlyList<(int X, int Y)> Points);

/// <summary>A parsed AutoCAD slide (.sld). Coordinates are pixels, origin bottom-left (y up).</summary>
public sealed record SlideDoc(int Level, int Width, int Height, double AspectRatio, IReadOnlyList<SlideShape> Shapes, int ColorChanges)
{
    public int VectorCount => Shapes.Count(s => !s.IsSolid);

    public int SolidCount => Shapes.Count(s => s.IsSolid);
}

/// <summary>
/// AutoCAD slide file reader. Layout (little endian): 17-byte signature "AutoCAD Slide\r\n\x1a\0", type byte (86), level byte
/// (2 = new, 1 = old), high-x, high-y (words), aspect ratio x1e6 (dword), fill indicator (word), byte-order test 0x1234 (word),
/// then records. A record starts with a word whose HIGH byte is the type:
///   0x00..0xFA long vector (x1 y1 x2 y2 as words), 0xFB offset vector, 0xFC end of file, 0xFD solid-fill vertex, 0xFE common-end-point vector,
///   0xFF color change (low byte = ACI color).
/// Solid fill: a vertex record with negative y opens the polygon (its point is a marker, not a vertex); following positive-y records are
/// vertices; the next negative-y record carries the last vertex (|y|).
/// Offset (0xFB) and common-end-point (0xFE) vectors are implemented from the format description but the legacy corpus contains
/// none of them (all 416 files are level 2 with long vectors only), so they are covered by synthetic tests only; they are
/// rejected in level-1 (old) slides, which only have long vectors, color changes and solid fills.
/// </summary>
public static class SlideReader
{
    private static readonly byte[] Signature = "AutoCAD Slide\r\n\u001a\0"u8.ToArray();

    public const int HeaderSize = 31;

    public static bool LooksLikeSlide(ReadOnlySpan<byte> data) => data.Length >= HeaderSize && data[..Signature.Length].SequenceEqual(Signature);

    public static SlideDoc Parse(byte[] d)
    {
        if (!LooksLikeSlide(d))
        {
            throw new InvalidDataException("not an AutoCAD slide (signature mismatch)");
        }

        var type = d[17];
        var level = d[18];
        if (level is not (1 or 2))
        {
            throw new InvalidDataException($"unsupported slide level {level} (type {type})");
        }

        int W(int o) => d[o] | (d[o + 1] << 8);
        int S(int o) => (short)(d[o] | (d[o + 1] << 8));
        var sx = W(19);
        var sy = W(21);
        var ratio = (uint)(d[23] | (d[24] << 8) | (d[25] << 16) | (d[26] << 24)) / 1_000_000.0;
        if (W(29) != 0x1234)
        {
            throw new InvalidDataException("slide byte-order test word is not 0x1234 (big-endian slides are not supported)");
        }

        var shapes = new List<SlideShape>();
        var color = 7;
        var colorChanges = 0;
        var p = HeaderSize;
        List<(int X, int Y)>? solid = null;
        (int X, int Y) s0 = (0, 0), e0 = (0, 0);
        var haveVec = false;
        var ended = false;

        void AddVec(int x1, int y1, int x2, int y2)
        {
            shapes.Add(new SlideShape(false, color, [(x1, y1), (x2, y2)]));
            s0 = (x1, y1);
            e0 = (x2, y2);
            haveVec = true;
        }

        while (p < d.Length && !ended)
        {
            if (p + 2 > d.Length)
            {
                throw new InvalidDataException($"truncated record at {p}");
            }

            var hi = d[p + 1];
            switch (hi)
            {
                case <= 0xFA:
                    Need(p, 8, d.Length);
                    AddVec(S(p), S(p + 2), S(p + 4), S(p + 6));
                    p += 8;
                    break;
                case 0xFB when level == 2:
                    Need(p, 5, d.Length);
                    if (!haveVec)
                    {
                        throw new InvalidDataException("offset vector before any vector");
                    }

                    // [dx1][FB][dy1][dx2][dy2], signed bytes; start offset from previous start, end offset from previous end.
                    AddVec(s0.X + (sbyte)d[p], s0.Y + (sbyte)d[p + 2], e0.X + (sbyte)d[p + 3], e0.Y + (sbyte)d[p + 4]);
                    p += 5;
                    break;
                case 0xFE when level == 2:
                    Need(p, 3, d.Length);
                    if (!haveVec)
                    {
                        throw new InvalidDataException("common-end-point vector before any vector");
                    }

                    // [dx][FE][dy]: starts at the previous end point.
                    AddVec(e0.X, e0.Y, e0.X + (sbyte)d[p], e0.Y + (sbyte)d[p + 2]);
                    p += 3;
                    break;
                case 0xFC:
                    ended = true;
                    p += 2;
                    break;
                case 0xFD:
                {
                    Need(p, 6, d.Length);
                    var x = S(p + 2);
                    var y = S(p + 4);
                    p += 6;
                    if (y < 0)
                    {
                        if (solid is null)
                        {
                            solid = [];
                        }
                        else
                        {
                            solid.Add((x, -y));
                            shapes.Add(new SlideShape(true, color, Order(solid)));
                            solid = null;
                        }
                    }
                    else
                    {
                        if (solid is null)
                        {
                            throw new InvalidDataException("solid vertex outside a solid-fill group");
                        }

                        solid.Add((x, y));
                    }

                    break;
                }

                case 0xFF:
                    color = d[p];
                    colorChanges++;
                    p += 2;
                    break;
                default:
                    throw new InvalidDataException($"record type 0x{hi:X2} is not valid in a level-{level} slide (offset {p})");
            }
        }

        if (!ended)
        {
            throw new InvalidDataException("missing end-of-file record (0xFC)");
        }

        if (solid is not null)
        {
            throw new InvalidDataException("unterminated solid fill");
        }

        return new SlideDoc(level, sx, sy, ratio, shapes, colorChanges);
    }

    private static void Need(int p, int n, int len)
    {
        if (p + n > len)
        {
            throw new InvalidDataException($"truncated record at {p}");
        }
    }

    // AutoCAD SOLID vertex order is p1 p2 p3 p4 with p3/p4 swapped for outline; triangles repeat the last point.
    private static IReadOnlyList<(int X, int Y)> Order(List<(int X, int Y)> pts)
    {
        if (pts.Count == 4 && pts[2] != pts[3])
        {
            return [pts[0], pts[1], pts[3], pts[2]];
        }

        return pts.Count == 4 ? [pts[0], pts[1], pts[2]] : pts.ToArray();
    }

    /// <summary>ACI color index to #rrggbb (standard AutoCAD palette: 1-9 fixed, 10-249 by hue/shade, 250-255 grays; 0/7/256 = white).</summary>
    public static string AciHex(int aci)
    {
        (int r, int g, int b) c;
        switch (aci)
        {
            case 1: c = (255, 0, 0); break;
            case 2: c = (255, 255, 0); break;
            case 3: c = (0, 255, 0); break;
            case 4: c = (0, 255, 255); break;
            case 5: c = (0, 0, 255); break;
            case 6: c = (255, 0, 255); break;
            case 8: c = (65, 65, 65); break;
            case 9: c = (128, 128, 128); break;
            case >= 10 and <= 249:
            {
                var n = aci - 10;
                var hue = n / 10 * 15.0;
                var k = n % 10;
                var sat = k % 2 == 0 ? 1.0 : 0.5;
                var val = new[] { 1.0, 0.65, 0.5, 0.3, 0.15 }[k / 2];
                c = Hsv(hue, sat, val);
                break;
            }

            case >= 250 and <= 255:
            {
                var v = new[] { 51, 91, 132, 173, 214, 255 }[aci - 250];
                c = (v, v, v);
                break;
            }

            default: c = (255, 255, 255); break;
        }

        return "#" + c.r.ToString("x2", CultureInfo.InvariantCulture) + c.g.ToString("x2", CultureInfo.InvariantCulture) + c.b.ToString("x2", CultureInfo.InvariantCulture);
    }

    private static (int, int, int) Hsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return ((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    /// <summary>Deterministic SVG (black screen background like AutoCAD, y axis flipped to screen space, consecutive same-color shapes merged).</summary>
    public static string ToSvg(SlideDoc s, bool background = true)
    {
        var w = Math.Max(1, s.Width + 1);
        var h = Math.Max(1, s.Height + 1);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w} {h}\" width=\"{w}\" height=\"{h}\">");
        if (background)
        {
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#000\"/>");
        }

        sb.Append(CultureInfo.InvariantCulture, $"<g transform=\"translate(0.5 {h - 0.5}) scale(1 -1)\" stroke-width=\"1\" stroke-linecap=\"round\" fill=\"none\">");
        for (var i = 0; i < s.Shapes.Count;)
        {
            var first = s.Shapes[i];
            var j = i;
            var d = new StringBuilder();
            while (j < s.Shapes.Count && s.Shapes[j].IsSolid == first.IsSolid && s.Shapes[j].Color == first.Color)
            {
                var pts = s.Shapes[j].Points;
                for (var k = 0; k < pts.Count; k++)
                {
                    d.Append(k == 0 ? 'M' : 'L').Append(pts[k].X.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(pts[k].Y.ToString(CultureInfo.InvariantCulture));
                }

                if (first.IsSolid)
                {
                    d.Append('Z');
                }

                j++;
            }

            var hex = AciHex(first.Color);
            sb.Append(first.IsSolid
                ? $"<path d=\"{d}\" fill=\"{hex}\" stroke=\"none\"/>"
                : $"<path d=\"{d}\" stroke=\"{hex}\" vector-effect=\"non-scaling-stroke\"/>");
            i = j;
        }

        sb.Append("</g></svg>");
        return sb.ToString();
    }
}
