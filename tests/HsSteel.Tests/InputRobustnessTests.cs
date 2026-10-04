using System.Text.Json.Nodes;
using HsSteel.Domain;
using HsSteel.Drafting;
using HsSteel.Mcp;
using HsSteel.Modeling;
using ModelContextProtocol;

namespace HsSteel.Tests;

/// <summary>
/// Bad project input (hand-edited JSON, hs_project_put, hs_member_add) must give a clear warning or a
/// client-visible McpException instead of crashing the build or writing NaN geometry. These tests need
/// no legacy assets, so they also run in CI.
/// </summary>
public class InputRobustnessTests
{
    private static Workspace NewWorkspace() =>
        Workspace.Create(
            Path.Combine(Path.GetTempPath(), "hs-no-assets-" + Guid.NewGuid().ToString("N")),
            Path.Combine(Path.GetTempPath(), "hs-ws-" + Guid.NewGuid().ToString("N")));

    private static Project TwoBeams() => new()
    {
        Name = "R1",
        Members =
        [
            new MemberDef("B1", AssemblyType.Beam, "H300x150x6.5x9", new V3(0, 0, 3000), new V3(6000, 0, 3000)),
            new MemberDef("C1", AssemblyType.Column, "H300x300x10x15", new V3(0, 0, 0), new V3(0, 0, 3000)),
        ],
    };

    private static void AssertFinite(ModelResult r)
    {
        Assert.All(r.ShapeParts, s => Assert.True(double.IsFinite(s.Length) && s.Length > 0, $"{s.Mark} length {s.Length}"));
    }

    [Fact]
    public void Zero_length_member_is_skipped_with_a_warning_instead_of_nan_geometry()
    {
        var ws = NewWorkspace();
        var p = TwoBeams();
        p.Members.Add(new MemberDef("Z1", AssemblyType.Beam, "H300x150x6.5x9", new V3(1000, 0, 3000), new V3(1000, 0, 3000)));
        var r = ws.Build(p);
        Assert.Contains(r.Warnings, w => w.StartsWith("Z1:", StringComparison.Ordinal));
        Assert.False(r.Profiles.ContainsKey("Z1"));
        Assert.True(r.Profiles.ContainsKey("B1"));
        AssertFinite(r);
    }

    [Fact]
    public void Duplicate_member_ids_warn_instead_of_crashing()
    {
        var ws = NewWorkspace();
        var p = TwoBeams();
        p.Members.Add(new MemberDef("B1", AssemblyType.Beam, "H400x200x8x13", new V3(0, 6000, 3000), new V3(6000, 6000, 3000)));
        var r = ws.Build(p);
        Assert.Contains(r.Warnings, w => w.Contains("duplicate member id", StringComparison.Ordinal));
        Assert.Equal("H300x150x6.5x9", r.Profiles["B1"].Spec);
    }

    [Fact]
    public void Unknown_section_skips_only_that_member()
    {
        var ws = NewWorkspace();
        var p = TwoBeams();
        p.Members.Add(new MemberDef("X1", AssemblyType.Beam, "NOT-A-SECTION", new V3(0, 0, 0), new V3(1000, 0, 0)));
        var r = ws.Build(p);
        Assert.Contains(r.Warnings, w => w.StartsWith("X1:", StringComparison.Ordinal) && w.Contains("NOT-A-SECTION", StringComparison.Ordinal));
        Assert.Equal(2, r.Profiles.Count);
    }

    [Fact]
    public void Drawings_still_generate_when_a_column_is_invalid()
    {
        // The erection plan used model.Profiles[id] for every column in the project, so a column the
        // builder had to drop crashed drawing generation with KeyNotFoundException.
        var ws = NewWorkspace();
        var p = TwoBeams();
        p.Members.Add(new MemberDef("C9", AssemblyType.Column, "NOT-A-SECTION", new V3(6000, 0, 0), new V3(6000, 0, 3000)));
        var set = ws.Drawings(p, DrawingSet.Kinds.All);
        Assert.NotEmpty(set.Sheets);
    }

    [Fact]
    public void Member_add_rejects_bad_input_with_client_visible_errors()
    {
        var ws = NewWorkspace();
        var tools = new HsTools(ws);
        tools.ProjectNew("M1");

        var numeric = Assert.Throws<McpException>(() => tools.MemberAdd("M1", "B1", "99", "H300x150x6.5x9", [0, 0, 0], [1000, 0, 0]));
        Assert.Contains("Unknown member type", numeric.Message);
        Assert.Throws<McpException>(() => tools.MemberAdd("M1", "B1", "beem", "H300x150x6.5x9", [0, 0, 0], [1000, 0, 0]));
        Assert.Throws<McpException>(() => tools.MemberAdd("M1", "B1", "beam", "H300x150x6.5x9", [0], [1000, 0, 0]));
        Assert.Throws<McpException>(() => tools.MemberAdd("M1", "B1", "beam", "H300x150x6.5x9", [0, 0, double.NaN], [1000, 0, 0]));
        Assert.Throws<McpException>(() => tools.MemberAdd("M1", "B1", "beam", "H300x150x6.5x9", [5, 5, 5], [5, 5, 5]));
        Assert.Throws<McpException>(() => tools.MemberAdd("M1", "B1", "beam", "NOT-A-SECTION", [0, 0, 0], [1000, 0, 0]));
        Assert.Throws<McpException>(() => tools.MemberAdd("M1", " ", "beam", "H300x150x6.5x9", [0, 0, 0], [1000, 0, 0]));

        var ok = JsonNode.Parse(tools.MemberAdd("M1", "B1", "crane_girder", "H300x150x6.5x9", [0, 0], [1000, 0]))!;
        Assert.Equal(1, ok["members"]!.GetValue<int>());
    }

    [Fact]
    public void Missing_project_and_bad_json_surface_as_mcp_errors()
    {
        var tools = new HsTools(NewWorkspace());
        var missing = Assert.Throws<McpException>(() => tools.ProjectGet("nope"));
        Assert.Contains("does not exist", missing.Message);
        Assert.Throws<McpException>(() => tools.ProjectPut("{ not json"));
        Assert.Throws<McpException>(() => tools.ProjectPut("{\"name\":\"\"}"));
        tools.ProjectNew("J1");
        Assert.Throws<McpException>(() => tools.ConnectionAdd("J1", "{\"kind\":\"weld\",\"id\":\"W1\"}"));
    }

    [Fact]
    public void Project_frame_validates_spans_and_storeys()
    {
        var tools = new HsTools(NewWorkspace());
        Assert.Throws<McpException>(() => tools.ProjectFrame("F1", [6000], [6000], []));
        Assert.Throws<McpException>(() => tools.ProjectFrame("F1", [6000, -1], [6000], [4000]));
        Assert.Throws<McpException>(() => tools.ProjectFrame("F1", [6000], [double.PositiveInfinity], [4000]));
        var summary = JsonNode.Parse(tools.ProjectFrame("F1", [6000], [6000], [4000]))!;
        Assert.NotNull(summary);
    }
}
