using HsSteel.Domain;

namespace HsSteel.Modeling;

/// <summary>Local frame of a member: X along the axis, Y the section depth direction, Z the width direction.</summary>
public sealed record MemberFrame(V3 Origin, V3 X, V3 Y, V3 Z)
{
    public static MemberFrame Of(MemberDef m)
    {
        var x = (m.End - m.Start).Unit;
        var roll = m.Roll * Math.PI / 180;
        V3 y;
        if (Math.Abs(x.Z) > 0.999)
        {
            y = new V3(Math.Cos(roll), Math.Sin(roll), 0); // vertical member: depth along global X rotated by roll
        }
        else
        {
            var up = new V3(0, 0, 1);
            var yy = (up - (x * V3.Dot(up, x))).Unit;
            var zz = Cross(x, yy);
            y = (yy * Math.Cos(roll)) + (zz * Math.Sin(roll));
        }

        return new MemberFrame(m.Start, x, y, Cross(x, y));
    }

    /// <summary>Global point → (along, up, side) relative to the axis start.</summary>
    public V3 ToLocal(V3 p)
    {
        var d = p - Origin;
        return new V3(V3.Dot(d, X), V3.Dot(d, Y), V3.Dot(d, Z));
    }

    public static V3 Cross(V3 a, V3 b) => new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
}

/// <summary>Output of <see cref="ModelBuilder"/>: numbered parts and assemblies, plus everything that was assumed.</summary>
public sealed class ModelResult
{
    public required Project Project { get; init; }

    public required DetailRules Rules { get; init; }

    public List<ShapePart> ShapeParts { get; } = [];

    public List<PlatePart> PlateParts { get; } = [];

    public List<Assembly> Assemblies { get; } = [];

    /// <summary>Member id → assembly mark.</summary>
    public Dictionary<string, string> MemberMarks { get; } = [];

    /// <summary>Member id → resolved profile.</summary>
    public Dictionary<string, Profile> Profiles { get; } = [];

    public List<string> Warnings { get; } = [];
}

/// <summary>
/// Turns a <see cref="Project"/> into fabrication parts: cut lengths and holes from connections,
/// connection plates, then numbering — identical parts share a mark, identical assemblies share a mark.
/// </summary>
public sealed class ModelBuilder(SectionCatalog catalog, SpliceStandards splices)
{
    private sealed class Work(MemberDef def, Profile profile, MemberFrame frame)
    {
        public MemberDef Def { get; } = def;

        public Profile Profile { get; } = profile;

        public MemberFrame Frame { get; } = frame;

        public double StartCut { get; set; }

        public double EndCut { get; set; }

        /// <summary>Holes measured from a member end (resolved once cuts are known).</summary>
        public List<(MemberEnd End, double FromEnd, HoleFace Face, double Across, double Dia)> EndHoles { get; } = [];

        public List<FlangeCope> Copes { get; } = [];

        /// <summary>Plates in local coordinates measured from the axis start (shifted by StartCut later).</summary>
        public List<(PlatePart Plate, Placement At, bool Welded)> Plates { get; } = [];

        public Dictionary<string, int> Bolts { get; } = [];

        public double Length => Def.AxisLength - StartCut - EndCut;
    }

    public ModelResult Build(Project project)
    {
        var rules = project.Rules ?? new DetailRules();
        var result = new ModelResult { Project = project, Rules = rules };
        var work = new Dictionary<string, Work>(StringComparer.Ordinal);
        foreach (var m in project.Members)
        {
            // Bad members are reported and left out instead of aborting the whole build: a duplicate
            // id used to throw from ToDictionary, an unknown section threw FormatException, and a
            // zero-length axis produced NaN frames that were written into the DXF/DWG output.
            if (string.IsNullOrWhiteSpace(m.Id))
            {
                result.Warnings.Add($"member with an empty id ({m.Section}) skipped.");
                continue;
            }

            if (work.ContainsKey(m.Id))
            {
                result.Warnings.Add($"{m.Id}: duplicate member id; the later definition is ignored.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(m.Section))
            {
                result.Warnings.Add($"{m.Id}: no section given; member skipped.");
                continue;
            }

            var length = m.AxisLength;
            if (!double.IsFinite(length) || length < 1e-6)
            {
                result.Warnings.Add($"{m.Id}: start and end coincide or are not finite; member skipped.");
                continue;
            }

            Profile profile;
            try
            {
                profile = catalog.Resolve(m.Section);
            }
            catch (FormatException ex)
            {
                result.Warnings.Add($"{m.Id}: {ex.Message} Member skipped.");
                continue;
            }

            work[m.Id] = new Work(m, profile, MemberFrame.Of(m));
        }

        foreach (var (id, w) in work)
        {
            result.Profiles[id] = w.Profile;
        }

        foreach (var c in project.Connections)
        {
            try
            {
                switch (c)
                {
                    case SpliceDef s: Splice(work[s.MemberA], work[s.MemberB], s, rules, result); break;
                    case ShearTabDef t: ShearTab(work[t.Beam], t.BeamEnd, work[t.Support], t, rules, result); break;
                    case BasePlateDef b: BasePlate(work[b.Column], b, rules); break;
                    case EndCapDef e: EndCap(work[e.Member], e, rules); break;
                case EndPlateDef ep: EndPlate(work[ep.Beam], ep.BeamEnd, work[ep.Support], ep, rules, result); break;
                }
            }
            catch (KeyNotFoundException ex)
            {
                result.Warnings.Add($"{c.Id}: {ex.Message}");
            }
        }

        Number(work.Values, result);
        return result;
    }

    // ---------------------------------------------------------------- connections

    private void Splice(Work a, Work b, SpliceDef s, DetailRules rules, ModelResult result)
    {
        var p = a.Profile;
        if (p.Kind != ShapeKind.I)
        {
            result.Warnings.Add($"{s.Id}: splice supports H sections only ({p.Spec}).");
            return;
        }

        var column = a.Def.Type is AssemblyType.Column or AssemblyType.SubColumn or AssemblyType.Post;
        var spec = splices.Find(p.Spec, column, s.BoltSize);
        if (spec is null)
        {
            result.Warnings.Add($"{s.Id}: no SCSS standard row for {p.Spec}; splice skipped.");
            return;
        }

        var gap = spec.Gap;
        a.EndCut += gap / 2;
        b.StartCut += gap / 2;
        var webHole = spec.WebBoltDia + 2;
        var flgHole = spec.FlangeBoltDia + 2;
        var webY0 = (p.Depth - spec.WebY.Total) / 2;
        var flangeLines = spec.FlangeLineOffsets(p.Width).ToArray();
        if (spec.WebBoltCountTable != spec.WebBoltCount)
        {
            result.Warnings.Add($"{s.Id}: {spec.Standard} {p.Spec} lists {spec.WebBoltCountTable} web bolts; layout gives {spec.WebBoltCount} (layout used).");
        }

        foreach (var (w, end) in new[] { (a, MemberEnd.End), (b, MemberEnd.Start) })
        {
            foreach (var x in spec.WebX.Holes)
            {
                foreach (var y in spec.WebY.Holes)
                {
                    w.EndHoles.Add((end, x, HoleFace.Web, webY0 + y, webHole));
                }
            }

            foreach (var x in spec.FlangeX.Holes)
            {
                foreach (var z in flangeLines)
                {
                    w.EndHoles.Add((end, x, HoleFace.TopFlange, z, flgHole));
                    w.EndHoles.Add((end, x, HoleFace.BottomFlange, z, flgHole));
                }
            }
        }

        // Plates belong (loose) to assembly A, positioned around the joint at x = A axis end.
        var joint = a.Def.AxisLength;
        var lw = (2 * spec.WebX.Total) + gap;
        var webHoles = Mirror(spec.WebX, lw).SelectMany(u => spec.WebY.Holes.Select(v => new PlateHole(u, v, webHole))).ToList();
        var web = PlatePart.Rect(spec.WebPlateT, lw, spec.WebY.Total, webHoles, "SPLICE-WEB", spec.Material);
        var x0 = joint - (lw / 2);
        a.Plates.Add((web, new Placement(PlateOrientation.Web, new V3(x0, webY0, (p.Width / 2) + (p.Tw / 2)), 1, 1), false));
        a.Plates.Add((web, new Placement(PlateOrientation.Web, new V3(x0, webY0, (p.Width / 2) - (p.Tw / 2)), 1, -1), false));

        var lf = (2 * spec.FlangeX.Total) + gap;
        var xf = joint - (lf / 2);
        var us = Mirror(spec.FlangeX, lf).ToList();
        var outer = PlatePart.Rect(spec.FlangeOuterT, lf, p.Width, us.SelectMany(u => flangeLines.Select(v => new PlateHole(u, v, flgHole))), "SPLICE-FLANGE-OUT", spec.Material);
        a.Plates.Add((outer, new Placement(PlateOrientation.Flange, new V3(xf, p.Depth, 0), 1, 1), false));
        a.Plates.Add((outer, new Placement(PlateOrientation.Flange, new V3(xf, 0, 0), 1, -1), false));

        var wi = Math.Max(30, ((p.Width - p.Tw) / 2) - Math.Max(p.Radius, 10) - 2);
        var leftLines = flangeLines.Where(z => z < p.Width / 2).ToList();
        var rightLines = flangeLines.Where(z => z > p.Width / 2).ToList();
        var innerL = PlatePart.Rect(spec.FlangeInnerT, lf, wi, us.SelectMany(u => leftLines.Select(z => new PlateHole(u, z, flgHole))), "SPLICE-FLANGE-IN", spec.Material);
        var innerR = PlatePart.Rect(spec.FlangeInnerT, lf, wi, us.SelectMany(u => rightLines.Select(z => new PlateHole(u, z - (p.Width - wi), flgHole))), "SPLICE-FLANGE-IN", spec.Material);
        foreach (var (y, n) in new[] { (p.Depth - p.Tf, -1), (p.Tf, 1) })
        {
            a.Plates.Add((innerL, new Placement(PlateOrientation.Flange, new V3(xf, y, 0), 1, n), false));
            a.Plates.Add((innerR, new Placement(PlateOrientation.Flange, new V3(xf, y, p.Width - wi), 1, n), false));
        }

        Add(a.Bolts, spec.WebBolt, spec.WebBoltCount);
        Add(a.Bolts, spec.FlangeBolt, spec.FlangeBoltCount);
    }

    private void ShearTab(Work beam, MemberEnd end, Work support, ShearTabDef t, DetailRules rules, ModelResult result)
    {
        var bp = beam.Profile;
        var sp = support.Profile;
        var spec = splices.Find(bp.Spec, false, t.BoltSize);
        var boltDia = spec?.WebBoltDia ?? (t.BoltSize > 0 ? t.BoltSize : 20);
        BoltAxis rowsAxis;
        IReadOnlyList<string> rowFlags;
        if (spec?.WebY is { } standardRows)
        {
            rowsAxis = standardRows;
            rowFlags = ShearTabLayout.Check(bp, rowsAxis);
        }
        else
        {
            // No splice-standard entry: rows that fit the clear web with code edge distances (ShearTabLayout).
            var layout = ShearTabLayout.Default(bp, boltDia);
            rowsAxis = layout.Rows;
            rowFlags = layout.Flags;
        }

        foreach (var flag in rowFlags)
        {
            result.Warnings.Add($"{t.Id}: beam {beam.Def.Id} ({bp.Spec}): {flag}");
        }

        var hole = boltDia + 2;
        var plateT = t.PlateT > 0 ? t.PlateT : spec?.WebPlateT ?? 9;
        var gap = rules.ConnectionGap;
        var e = rules.EndGauge;

        var joint = end == MemberEnd.Start ? beam.Def.Start : beam.Def.End;
        var other = end == MemberEnd.Start ? beam.Def.End : beam.Def.Start;
        var f = support.Frame;
        var local = f.ToLocal(joint);
        var dir = (other - joint).Unit;
        var (bx, by, bz) = (V3.Dot(dir, f.X), V3.Dot(dir, f.Y), V3.Dot(dir, f.Z));
        var vertical = Math.Abs(f.X.Z) > 0.7;          // support is a column
        var alongDepth = vertical && Math.Abs(by) > Math.Abs(bz); // beam hits the column flange face
        var side = (alongDepth ? by : bz) >= 0 ? 1 : -1;
        var face = alongDepth ? sp.Depth / 2 : sp.Tw / 2; // support face distance from its axis
        var width = gap + (2 * e);                     // plate size along the beam
        var height = rowsAxis.Total;                   // plate size vertically

        // Beam end: cut back from the support axis to the face + gap.
        var cut = face + gap;
        if (end == MemberEnd.Start)
        {
            beam.StartCut += cut;
        }
        else
        {
            beam.EndCut += cut;
        }

        var y0 = (bp.Depth - rowsAxis.Total) / 2;
        foreach (var y in rowsAxis.Holes)
        {
            beam.EndHoles.Add((end, e, HoleFace.Web, y0 + y, hole));
        }

        // Bolt line distance from the near plate edge (the welded edge), mirrored on the negative side.
        var boltAlong = side > 0 ? gap + e : width - (gap + e);
        if (!vertical)
        {
            // Horizontal support (girder): plate perpendicular to the support axis → End orientation.
            var plate = PlatePart.Rect(plateT, width, height, rowsAxis.Holes.Select(v => new PlateHole(gap + e, v, hole)), "SHEAR-TAB", rules.Material);
            var origin = new V3(local.X - (plateT / 2), (sp.Depth / 2) + local.Y - (height / 2), (sp.Width / 2) + (side * sp.Tw / 2));
            support.Plates.Add((plate, new Placement(PlateOrientation.End, origin, side, 1), true));
            var topOver = (Math.Abs(local.Y) + (bp.Depth / 2)) - (sp.Depth / 2);
            if (topOver > 1e-6)
            {
                var copeLen = Math.Round((sp.Width / 2) + gap + rules.WeldGap, 1);
                var copeDepth = Math.Round(Math.Max(bp.Tf + bp.Radius, rules.Scallop), 1);
                var cornerTop = end == MemberEnd.Start ? CopeCorner.TopStart : CopeCorner.TopEnd;
                var cornerBot = end == MemberEnd.Start ? CopeCorner.BottomStart : CopeCorner.BottomEnd;
                beam.Copes.Add(new FlangeCope(cornerTop, copeLen, copeDepth, rules.Scallop));
                beam.Copes.Add(new FlangeCope(cornerBot, copeLen, copeDepth, rules.Scallop));
            }
        }
        else
        {
            // Column: plate is vertical (u along the column axis) and runs along the beam (v).
            var holes = rowsAxis.Holes.Select(v => new PlateHole(v, boltAlong, hole));
            var plate = PlatePart.Rect(plateT, height, width, holes, "SHEAR-TAB", rules.Material);
            var u0 = local.X - (height / 2);
            if (alongDepth)
            {
                var y = side > 0 ? sp.Depth : -width;
                var z = (sp.Width / 2) + local.Z - (plateT / 2);
                support.Plates.Add((plate, new Placement(PlateOrientation.Web, new V3(u0, y, z), 1, 1), true));
            }
            else
            {
                var z = side > 0 ? (sp.Width / 2) + (sp.Tw / 2) : (sp.Width / 2) - (sp.Tw / 2) - width;
                var y = (sp.Depth / 2) + local.Y - (plateT / 2);
                support.Plates.Add((plate, new Placement(PlateOrientation.Flange, new V3(u0, y, z), 1, 1), true));
            }
        }

        Add(beam.Bolts, $"TS M{boltDia:0}", rowsAxis.Count);
        _ = bx;
    }

    private void EndPlate(Work beam, MemberEnd end, Work support, EndPlateDef t, DetailRules rules, ModelResult result)
    {
        var bp = beam.Profile;
        var sp = support.Profile;
        var spec = splices.Find(bp.Spec, false, t.BoltSize);
        var boltDia = spec?.WebBoltDia ?? (t.BoltSize > 0 ? t.BoltSize : 20);
        BoltAxis rowsAxis;
        IReadOnlyList<string> rowFlags;
        if (spec?.WebY is { } standardRows)
        {
            rowsAxis = standardRows;
            rowFlags = ShearTabLayout.Check(bp, rowsAxis);
        }
        else
        {
            var layout = ShearTabLayout.Default(bp, boltDia);
            rowsAxis = layout.Rows;
            rowFlags = layout.Flags;
        }

        foreach (var flag in rowFlags)
        {
            result.Warnings.Add($"{t.Id}: beam {beam.Def.Id} ({bp.Spec}): {flag}");
        }

        var hole = boltDia + 2;
        var plateT = t.PlateT > 0 ? t.PlateT : spec?.WebPlateT ?? 12;
        var ext = Math.Max(0, t.Extension);
        var e = rules.EndGauge;
        var gap = rules.ConnectionGap;

        // Cut beam back to the support face + gap; the end plate thickness sits in that gap zone on the beam end.
        var f = support.Frame;
        var joint = end == MemberEnd.Start ? beam.Def.Start : beam.Def.End;
        var local = f.ToLocal(joint);
        var vertical = Math.Abs(f.X.Z) > 0.7;
        var face = vertical ? sp.Depth / 2 : sp.Tw / 2;
        var cut = face + gap;
        if (end == MemberEnd.Start) { beam.StartCut += cut; } else { beam.EndCut += cut; }

        var y0 = (bp.Depth - rowsAxis.Total) / 2;
        foreach (var y in rowsAxis.Holes)
        {
            beam.EndHoles.Add((end, e, HoleFace.Web, y0 + y, hole));
        }

        var u = bp.Width + (2 * ext);
        var v = bp.Depth + (2 * ext);
        var holes = rowsAxis.Holes.Select(y => new PlateHole(ext + (bp.Width / 2), ext + y0 + y, hole)).ToList();
        var plate = PlatePart.Rect(plateT, u, v, holes, "END-PLATE", rules.Material);
        var x = end == MemberEnd.Start ? -plateT : beam.Def.AxisLength;
        beam.Plates.Add((plate, new Placement(PlateOrientation.End, new V3(x, -ext, -ext), 1, 1), true));
        Add(beam.Bolts, $"TS M{boltDia:0}", rowsAxis.Count);
        _ = local;
    }

    private static void BasePlate(Work col, BasePlateDef b, DetailRules rules)
    {
        var p = col.Profile;
        double u = p.Width + (2 * b.Margin), v = p.Depth + (2 * b.Margin), e = b.AnchorEdge;
        PlateHole[] holes = [new(e, e, b.AnchorDia), new(u - e, e, b.AnchorDia), new(e, v - e, b.AnchorDia), new(u - e, v - e, b.AnchorDia)];
        var plate = PlatePart.Rect(b.Thickness, u, v, holes, "BASE", rules.Material);
        col.Plates.Add((plate, new Placement(PlateOrientation.End, new V3(-b.Thickness, -b.Margin, -b.Margin), 1, 1), true));
    }

    private static void EndCap(Work m, EndCapDef c, DetailRules rules)
    {
        var p = m.Profile;
        var plate = PlatePart.Rect(c.Thickness, p.Width, p.Depth, [], "CAP", rules.Material);
        var x = c.End == MemberEnd.Start ? -c.Thickness : m.Def.AxisLength;
        m.Plates.Add((plate, new Placement(PlateOrientation.End, new V3(x, 0, 0), 1, 1), true));
    }

    /// <summary>Hole positions of a per-side axis mirrored about the centre of a plate of length <paramref name="total"/>.</summary>
    private static IEnumerable<double> Mirror(BoltAxis axis, double total)
    {
        // Member holes are measured from the member end; the plate is centred on the joint.
        var half = axis.Holes.ToList();
        return half.Select(h => axis.Total - h).Concat(half.Select(h => total - axis.Total + h)).Select(v => Math.Round(v, 3)).Order();
    }

    private static void Add(Dictionary<string, int> bolts, string name, int n)
    {
        if (n > 0)
        {
            bolts[name] = bolts.GetValueOrDefault(name) + n;
        }
    }

    // ---------------------------------------------------------------- numbering

    private static void Number(IEnumerable<Work> works, ModelResult result)
    {
        var shapes = new Dictionary<string, ShapePart>(StringComparer.Ordinal);
        var plates = new Dictionary<string, PlatePart>(StringComparer.Ordinal);
        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);

        PlatePart Intern(PlatePart p)
        {
            if (!plates.TryGetValue(p.Signature, out var u))
            {
                u = p;
                u.Mark = $"P{plates.Count + 1}";
                plates[p.Signature] = u;
            }

            return u;
        }

        foreach (var w in works.OrderBy(w => w.Def.Type).ThenBy(w => w.Def.Id, StringComparer.Ordinal))
        {
            var len = Math.Round(w.Length, 1);
            var holes = w.EndHoles
                .Select(h => new Hole(h.Face, Math.Round(h.End == MemberEnd.Start ? h.FromEnd : len - h.FromEnd, 2), Math.Round(h.Across, 2), h.Dia))
                .OrderBy(h => h.Face).ThenBy(h => h.X).ThenBy(h => h.Across)
                .ToList();
            var copes = w.Copes
                .Select(c => c with { Length = Math.Round(c.Length, 1), Depth = Math.Round(c.Depth, 1), Radius = Math.Round(c.Radius, 1) })
                .OrderBy(c => c.Corner).ThenBy(c => c.Length).ThenBy(c => c.Depth)
                .ToList();
            var candidate = new ShapePart { Profile = w.Profile, Length = len, Holes = holes, Copes = copes, Material = w.Def.Material ?? result.Rules.Material };
            if (!shapes.TryGetValue(candidate.Signature, out var shape))
            {
                shape = candidate;
                shape.Mark = $"S{shapes.Count + 1}";
                shapes[candidate.Signature] = shape;
            }

            shape.Quantity++;
            var attachments = new List<Attachment>();
            foreach (var (plate, at, welded) in w.Plates)
            {
                var p = Intern(plate);
                p.Quantity++;
                var shifted = at with { Origin = at.Origin - new V3(w.StartCut, 0, 0) };
                attachments.Add(new Attachment(p, shifted, welded));
            }

            var asm = new Assembly
            {
                Type = w.Def.Type,
                Main = shape,
                Attachments = attachments,
                Bolts = w.Bolts.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => new BoltSet(k.Key, k.Value)).ToList(),
            };
            if (!assemblies.TryGetValue(asm.Signature, out var existing))
            {
                var n = assemblies.Values.Count(x => x.Type == asm.Type) + 1;
                asm.Mark = $"{AssemblyTypes.Prefix(asm.Type)}{n}";
                assemblies[asm.Signature] = asm;
                existing = asm;
            }

            existing.Members.Add(w.Def.Id);
            result.MemberMarks[w.Def.Id] = existing.Mark;
        }

        result.ShapeParts.AddRange(shapes.Values);
        result.PlateParts.AddRange(plates.Values);
        result.Assemblies.AddRange(assemblies.Values);
    }
}
