using ACadSharp.Entities;

namespace HsSteel.Assets.Blocks;

/// <summary>Flattens entities to 2D segments/points; shared by extents and preview.</summary>
internal static class BlockGeometry
{
    public record struct P(double X, double Y);

    public static void Accumulate(IEnumerable<Entity> ents, Extents2 ext)
    {
        var list = ents.ToList();
        foreach (var (a, b) in Segments(list, 0)) { ext.Add(a.X, a.Y); ext.Add(b.X, b.Y); }
        foreach (var p in Points(list, 0)) ext.Add(p.X, p.Y);
    }

    private static P Xf(P p, Insert ins)
    {
        double x = p.X * ins.XScale, y = p.Y * ins.YScale, c = Math.Cos(ins.Rotation), s = Math.Sin(ins.Rotation);
        return new P(x * c - y * s + ins.InsertPoint.X, x * s + y * c + ins.InsertPoint.Y);
    }

    public static IEnumerable<P> Points(IEnumerable<Entity> ents, int depth)
    {
        foreach (var e in ents)
        {
            switch (e)
            {
                case TextEntity t: yield return new P(t.InsertPoint.X, t.InsertPoint.Y); break;
                case MText m: yield return new P(m.InsertPoint.X, m.InsertPoint.Y); break;
                case Point pt: yield return new P(pt.Location.X, pt.Location.Y); break;
                case Insert ins:
                    var inner = depth < 4 && ins.Block is not null ? Points(ins.Block.Entities, depth + 1).ToList() : new List<P>();
                    if (inner.Count == 0) yield return new P(ins.InsertPoint.X, ins.InsertPoint.Y);
                    foreach (var p in inner) yield return Xf(p, ins);
                    break;
            }
        }
    }

    public static IEnumerable<(P, P)> Segments(IEnumerable<Entity> ents, int depth)
    {
        foreach (var e in ents)
        {
            switch (e)
            {
                case Line l:
                    yield return (new P(l.StartPoint.X, l.StartPoint.Y), new P(l.EndPoint.X, l.EndPoint.Y));
                    break;
                case Arc a:
                    foreach (var s in Arcish(a.Center.X, a.Center.Y, a.Radius, a.StartAngle, a.EndAngle, false)) yield return s;
                    break;
                case Circle c:
                    foreach (var s in Arcish(c.Center.X, c.Center.Y, c.Radius, 0, 2 * Math.PI, true)) yield return s;
                    break;
                case Ellipse el:
                    foreach (var s in Poly(el.PolygonalVertexes(48).Select(v => new P(v.X, v.Y)).ToList(), false)) yield return s;
                    break;
                case LwPolyline lw:
                    foreach (var s in Poly(lw.Vertices.Select(v => new P(v.Location.X, v.Location.Y)).ToList(), lw.IsClosed)) yield return s;
                    break;
                case Polyline2D p2:
                    foreach (var s in Poly(p2.Vertices.Select(v => new P(v.Location.X, v.Location.Y)).ToList(), p2.IsClosed)) yield return s;
                    break;
                case Polyline3D p3:
                    foreach (var s in Poly(p3.Vertices.Select(v => new P(v.Location.X, v.Location.Y)).ToList(), p3.IsClosed)) yield return s;
                    break;
                case Solid so:
                    foreach (var s in Poly(new List<P>
                    {
                        new(so.FirstCorner.X, so.FirstCorner.Y), new(so.SecondCorner.X, so.SecondCorner.Y),
                        new(so.FourthCorner.X, so.FourthCorner.Y), new(so.ThirdCorner.X, so.ThirdCorner.Y)
                    }, true)) yield return s;
                    break;
                case Insert ins when depth < 4 && ins.Block is not null:
                    foreach (var (a2, b2) in Segments(ins.Block.Entities, depth + 1))
                        yield return (Xf(a2, ins), Xf(b2, ins));
                    break;
            }
        }
    }

    private static IEnumerable<(P, P)> Poly(List<P> pts, bool closed)
    {
        for (int i = 0; i + 1 < pts.Count; i++) yield return (pts[i], pts[i + 1]);
        if (closed && pts.Count > 2) yield return (pts[^1], pts[0]);
    }

    private static IEnumerable<(P, P)> Arcish(double cx, double cy, double r, double a0, double a1, bool full)
    {
        if (!double.IsFinite(r) || !double.IsFinite(a0) || !double.IsFinite(a1)) yield break;
        double sweep = full ? 2 * Math.PI : a1 - a0;
        if (!full) { while (sweep <= 0) sweep += 2 * Math.PI; }
        int n = Math.Max(8, (int)(sweep / (2 * Math.PI) * 48));
        P prev = new(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0));
        for (int i = 1; i <= n; i++)
        {
            double t = a0 + sweep * i / n;
            var cur = new P(cx + r * Math.Cos(t), cy + r * Math.Sin(t));
            yield return (prev, cur);
            prev = cur;
        }
    }
}
