using HsSteel.Domain;

namespace HsSteel.Modeling;

/// <summary>Parameters for a regular multi-storey steel frame on a rectangular grid.</summary>
/// <param name="SpansX">Bay widths along X (mm), e.g. [6000, 6000].</param>
/// <param name="SpansY">Bay widths along Y (mm).</param>
/// <param name="StoreyHeights">Storey heights from the base (mm), e.g. [4500, 4000].</param>
/// <param name="Column">Column section.</param>
/// <param name="GirderX">Girder section for members along X.</param>
/// <param name="GirderY">Girder section for members along Y.</param>
/// <param name="MaxColumnPiece">Columns longer than this are split with SCSS splices (0 = never).</param>
/// <param name="SubBeams">Number of secondary beams per bay (parallel to Y, framed into the X girders).</param>
/// <param name="SubBeam">Secondary beam section.</param>
public sealed record FrameSpec(
    IReadOnlyList<double> SpansX,
    IReadOnlyList<double> SpansY,
    IReadOnlyList<double> StoreyHeights,
    string Column = "H300x300x10x15",
    string GirderX = "H400x200x8x13",
    string GirderY = "H400x200x8x13",
    double MaxColumnPiece = 0,
    int SubBeams = 0,
    string SubBeam = "H300x150x6.5x9");

/// <summary>Generates complete project models (grids, levels, members, connections) from a few parameters.</summary>
public static class ProjectTemplates
{
    public static Project Frame(string name, FrameSpec f, DetailRules? rules = null)
    {
        var p = new Project { Name = name, Rules = rules };
        double x = 0;
        p.GridX.Add(new GridLine("X1", 0));
        for (var i = 0; i < f.SpansX.Count; i++)
        {
            x += f.SpansX[i];
            p.GridX.Add(new GridLine($"X{i + 2}", x));
        }

        double y = 0;
        p.GridY.Add(new GridLine("Y1", 0));
        for (var i = 0; i < f.SpansY.Count; i++)
        {
            y += f.SpansY[i];
            p.GridY.Add(new GridLine($"Y{i + 2}", y));
        }

        double z = 0;
        p.Levels.Add(new Level("BASE", 0));
        for (var i = 0; i < f.StoreyHeights.Count; i++)
        {
            z += f.StoreyHeights[i];
            p.Levels.Add(new Level($"{i + 2}F", z));
        }

        var top = p.Levels[^1].Elevation;
        var n = 0;
        string Id(string prefix) => $"{prefix}{++n}";

        // Columns (optionally split into pieces with splices just above a floor level).
        foreach (var gx in p.GridX)
        {
            foreach (var gy in p.GridY)
            {
                var cuts = new List<double> { 0 };
                if (f.MaxColumnPiece > 0)
                {
                    // Greedy: while the rest is too long, cut at the highest "floor + 1 m" within reach.
                    var candidates = p.Levels.Skip(1).SkipLast(1).Select(l => l.Elevation + 1000).Where(c => c < top).ToList();
                    var last = 0.0;
                    while (top - last > f.MaxColumnPiece)
                    {
                        var c = candidates.Where(v => v > last + 1 && v - last <= f.MaxColumnPiece).DefaultIfEmpty(double.NaN).Max();
                        if (double.IsNaN(c))
                        {
                            break;
                        }

                        cuts.Add(c);
                        last = c;
                    }
                }

                cuts.Add(top);
                string? prev = null;
                for (var i = 0; i + 1 < cuts.Count; i++)
                {
                    var id = Id("C");
                    p.Members.Add(new MemberDef(id, AssemblyType.Column, f.Column, new V3(gx.Position, gy.Position, cuts[i]), new V3(gx.Position, gy.Position, cuts[i + 1])));
                    if (i == 0)
                    {
                        p.Connections.Add(new BasePlateDef($"BP-{id}", id));
                    }

                    if (prev is not null)
                    {
                        p.Connections.Add(new SpliceDef($"SP-{prev}-{id}", prev, id));
                    }

                    prev = id;
                }

                p.Connections.Add(new EndCapDef($"CAP-{prev}", prev!, MemberEnd.End));
            }
        }

        string ColumnAt(double cx, double cy, double cz) =>
            p.Members.First(m => m.Type == AssemblyType.Column && Math.Abs(m.Start.X - cx) < 1 && Math.Abs(m.Start.Y - cy) < 1 && m.Start.Z <= cz + 1 && m.End.Z >= cz - 1).Id;

        // Girders between columns on every floor above the base, framed with shear tabs.
        foreach (var lv in p.Levels.Skip(1))
        {
            var gz = lv.Elevation; // girder axis at the level (centre-line model)
            foreach (var gy in p.GridY)
            {
                for (var i = 0; i + 1 < p.GridX.Count; i++)
                {
                    var a = new V3(p.GridX[i].Position, gy.Position, gz);
                    var b = new V3(p.GridX[i + 1].Position, gy.Position, gz);
                    var id = Id("G");
                    p.Members.Add(new MemberDef(id, AssemblyType.Girder, f.GirderX, a, b));
                    p.Connections.Add(new ShearTabDef($"ST-{id}-S", id, MemberEnd.Start, ColumnAt(a.X, a.Y, gz)));
                    p.Connections.Add(new ShearTabDef($"ST-{id}-E", id, MemberEnd.End, ColumnAt(b.X, b.Y, gz)));
                }
            }

            foreach (var gx in p.GridX)
            {
                for (var j = 0; j + 1 < p.GridY.Count; j++)
                {
                    var a = new V3(gx.Position, p.GridY[j].Position, gz);
                    var b = new V3(gx.Position, p.GridY[j + 1].Position, gz);
                    var id = Id("G");
                    p.Members.Add(new MemberDef(id, AssemblyType.Girder, f.GirderY, a, b));
                    p.Connections.Add(new ShearTabDef($"ST-{id}-S", id, MemberEnd.Start, ColumnAt(a.X, a.Y, gz)));
                    p.Connections.Add(new ShearTabDef($"ST-{id}-E", id, MemberEnd.End, ColumnAt(b.X, b.Y, gz)));
                }
            }

            // Secondary beams: parallel to Y inside each X bay, framed into the X girders.
            if (f.SubBeams > 0)
            {
                for (var i = 0; i + 1 < p.GridX.Count; i++)
                {
                    var x0 = p.GridX[i].Position;
                    var span = p.GridX[i + 1].Position - x0;
                    for (var k = 1; k <= f.SubBeams; k++)
                    {
                        var bx = x0 + (span * k / (f.SubBeams + 1));
                        for (var j = 0; j + 1 < p.GridY.Count; j++)
                        {
                            var ya = p.GridY[j].Position;
                            var yb = p.GridY[j + 1].Position;
                            var ga = p.Members.First(m => m.Type == AssemblyType.Girder && Math.Abs(m.Start.Y - ya) < 1 && Math.Abs(m.End.Y - ya) < 1 && Math.Abs(m.Start.Z - gz) < 1 && Math.Min(m.Start.X, m.End.X) < bx && Math.Max(m.Start.X, m.End.X) > bx).Id;
                            var gb = p.Members.First(m => m.Type == AssemblyType.Girder && Math.Abs(m.Start.Y - yb) < 1 && Math.Abs(m.End.Y - yb) < 1 && Math.Abs(m.Start.Z - gz) < 1 && Math.Min(m.Start.X, m.End.X) < bx && Math.Max(m.Start.X, m.End.X) > bx).Id;
                            var id = Id("B");
                            p.Members.Add(new MemberDef(id, AssemblyType.Beam, f.SubBeam, new V3(bx, ya, gz), new V3(bx, yb, gz)));
                            p.Connections.Add(new ShearTabDef($"ST-{id}-S", id, MemberEnd.Start, ga));
                            p.Connections.Add(new ShearTabDef($"ST-{id}-E", id, MemberEnd.End, gb));
                        }
                    }
                }
            }
        }

        return p;
    }
}
