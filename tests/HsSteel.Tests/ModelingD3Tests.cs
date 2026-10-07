using HsSteel.Domain;
using HsSteel.Modeling;

namespace HsSteel.Tests;

/// <summary>D3 Modeling slice: end-plate connections, flange copes/scallops, mark consolidation from grid framing.</summary>
public sealed class ModelingD3Tests
{
    [LegacyAssetFact]
    public void EndPlate_frame_produces_end_plate_parts_and_shared_marks()
    {
        var p = ProjectTemplates.Frame("EP-FRAME", new FrameSpec([6000], [6000], [4000], BeamConnection: BeamConnectionKind.EndPlate));
        Assert.Contains(p.Connections, c => c is EndPlateDef);
        Assert.DoesNotContain(p.Connections, c => c is ShearTabDef);

        var r = Fx.Ws.Value.Build(p);
        Assert.Contains(r.PlateParts, pl => pl.Role == "END-PLATE");
        Assert.DoesNotContain(r.PlateParts, pl => pl.Role == "SHEAR-TAB");
        Assert.True(r.Assemblies.Count >= 1);
        var girderIds = p.Members.Where(m => m.Type == AssemblyType.Girder).Select(m => m.Id).ToList();
        var girderMarks = girderIds.Select(id => r.MemberMarks[id]).Distinct().Count();
        Assert.True(girderMarks <= girderIds.Count);
        Assert.All(r.ShapeParts.Where(s => s.Holes.Count > 0), s => Assert.All(s.Holes, h => Assert.True(h.Dia > 0)));
    }

    [LegacyAssetFact]
    public void ShearTab_into_shallower_girder_generates_flange_copes_with_scallop()
    {
        var p = new Project
        {
            Name = "COPE",
            Rules = new DetailRules(Scallop: 35, ConnectionGap: 10, WeldGap: 5),
            Members =
            [
                new MemberDef("G1", AssemblyType.Girder, "H300x150x6.5x9", new V3(0, 0, 4000), new V3(6000, 0, 4000)),
                new MemberDef("B1", AssemblyType.Beam, "H400x200x8x13", new V3(3000, 0, 4000), new V3(3000, 5000, 4000)),
            ],
            Connections =
            [
                new ShearTabDef("ST1", "B1", MemberEnd.Start, "G1"),
            ],
        };

        var r = Fx.Ws.Value.Build(p);
        var beam = r.ShapeParts.First(s => s.Profile.Spec.StartsWith("H400", StringComparison.Ordinal));
        Assert.True(beam.Copes.Count >= 2, $"expected flange copes, got {beam.Copes.Count}; warnings={string.Join(" | ", r.Warnings)}");
        Assert.All(beam.Copes, c => Assert.Equal(35, c.Radius));
        Assert.Contains("COPE:", beam.Signature, StringComparison.Ordinal);
    }

    [LegacyAssetFact]
    public void Identical_members_with_same_geometry_share_one_mark()
    {
        var p = ProjectTemplates.Frame("MARKS", new FrameSpec([6000, 6000], [8000], [4000], SubBeams: 1));
        var r = Fx.Ws.Value.Build(p);
        Assert.Equal(r.ShapeParts.Select(s => s.Signature).Distinct(StringComparer.Ordinal).Count(), r.ShapeParts.Count);
        var beamIds = p.Members.Where(m => m.Type == AssemblyType.Beam).Select(m => m.Id).ToList();
        Assert.True(beamIds.Count >= 2);
        var marks = beamIds.Select(id => r.MemberMarks[id]).Distinct().Count();
        Assert.True(marks < beamIds.Count, $"expected mark consolidation for beams: {marks} marks / {beamIds.Count} members");
    }
}