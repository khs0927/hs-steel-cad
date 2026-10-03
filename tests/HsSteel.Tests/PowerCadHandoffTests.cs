using System.Text.Json.Nodes;
using HsSteel.Drafting;

namespace HsSteel.Tests;

public sealed class PowerCadHandoffTests
{
    private static DrawPlan Sample()
    {
        var plan = new DrawPlan { Title = "A-001", Scale = 10 };
        plan.Meta["kind"] = "part";
        plan.CurrentTag = new JsonObject
        {
            ["mark"] = "B1",
            ["spec"] = "H400x200x8x13",
        };
        plan.Line("STEEL", 0, 0, 1000, 0);
        return plan;
    }

    [Fact]
    public void Handoff_separates_create_spec_from_hs_tag()
    {
        var handoff = Sample().ToPowerCadHandoff();
        var row = handoff["entities"]!.AsArray().Single()!.AsObject();
        var spec = row["spec"]!.AsObject();
        var tag = row["tag"]!.AsObject();

        Assert.Equal("hs-steel-draw-plan/1", handoff["schema"]!.GetValue<string>());
        Assert.Equal("khs0927/hs-steel-cad", handoff["producer"]!.GetValue<string>());
        Assert.Equal("mm", handoff["units"]!.GetValue<string>());
        Assert.False(handoff["execution_authorized"]!.GetValue<bool>());
        Assert.False(handoff["may_execute_mutation"]!.GetValue<bool>());
        Assert.True(handoff["requires_live_document_binding"]!.GetValue<bool>());
        Assert.True(handoff["tags_require_xdata_persistence"]!.GetValue<bool>());

        Assert.Equal("line", spec["type"]!.GetValue<string>());
        Assert.False(spec.ContainsKey("hs"));
        Assert.Equal("B1", tag["mark"]!.GetValue<string>());
        Assert.Equal(64, handoff["contract_digest"]!.GetValue<string>().Length);
    }

    [Fact]
    public void Handoff_digest_is_deterministic_and_covers_tags()
    {
        var first = Sample().ToPowerCadHandoff();
        var second = Sample().ToPowerCadHandoff();
        Assert.Equal(
            first["contract_digest"]!.GetValue<string>(),
            second["contract_digest"]!.GetValue<string>());

        var changed = Sample();
        changed.CurrentTag = new JsonObject { ["mark"] = "B2" };
        changed.Line("STEEL", 0, 100, 1000, 100);
        Assert.NotEqual(
            first["contract_digest"]!.GetValue<string>(),
            changed.ToPowerCadHandoff()["contract_digest"]!.GetValue<string>());
    }
}
