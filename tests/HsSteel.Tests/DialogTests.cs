using HsSteel.Assets.Dialogs;

namespace HsSteel.Tests;

public class DialogTests
{
    private const string Sample = """
        // comment
        @include "base.dcl"
        /* block
           comment */
        row : cluster { layout = horizontal; }
        my_dialog : dialog {
            label = "Test \"dlg\"";
            : edit_box { key = "dim1"; label = "Text : "; value = "abc"; edit_width = 50; }
            : popup_list { key = "kind"; list = "A\nB\nC"; value = "1"; }
            : row { : toggle { key = "t1"; label = "Flag"; } : button { key = "go"; label = "Go"; is_default = true; } }
            ok_cancel ;
        }
        """;

    [Fact]
    public void ParsesDefinitionsTilesAttributesAndReferences()
    {
        var f = DclParser.Parse(Sample);
        Assert.Equal(["base.dcl"], f.Includes);
        Assert.Equal(["row", "my_dialog"], f.Definitions.Select(d => d.Name));
        Assert.Equal("prototype", f.Definitions[0].Kind);
        var d = f.Definitions[1];
        Assert.Equal("dialog", d.Kind);
        Assert.Equal("Test \"dlg\"", d.Label);
        Assert.Equal(["dialog", "edit_box", "popup_list", "row", "toggle", "button", "ok_cancel"], d.Tiles.Select(t => t.Type));
        var edit = d.Tiles.Single(t => t.Type == "edit_box");
        Assert.Equal(("dim1", "Text : ", "abc"), (edit.Key, edit.Label, edit.Default));
        Assert.Equal("50", edit.Attributes["edit_width"]);
        Assert.Equal(["A", "B", "C"], d.Tiles.Single(t => t.Type == "popup_list").ListItems);
        Assert.True(d.Tiles.Single(t => t.Type == "ok_cancel").IsReference);
        Assert.Equal(2, d.Tiles.Single(t => t.Type == "toggle").Depth);
        Assert.Empty(f.Warnings);
    }

    [Fact]
    public void ToleratesMalformedInput()
    {
        var f = DclParser.Parse("x : dialog { label = \"a\"; : button { key = \"k\";");
        Assert.Single(f.Definitions);
        Assert.NotEmpty(f.Warnings);
    }

    [Fact]
    public void DecodesCp949()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var bytes = System.Text.Encoding.GetEncoding(949).GetBytes("d : dialog { label = \"치수문자\"; }");
        Assert.Equal("치수문자", DclParser.Parse(DclParser.Decode(bytes)).Definitions[0].Label);
    }

    [Fact]
    public void LegacyDclFilesParse()
    {
        var f1 = @"C:\HS-STEEL\HSSTEEL\support\DIM_EDIT.DCL";
        if (File.Exists(f1))
        {
            var d = DclParser.Parse(DclParser.Decode(File.ReadAllBytes(f1))).Definitions.Single();
            Assert.Equal("dim_edit", d.Name);
            Assert.Contains(d.Tiles, t => t.Key == "dum1" && t.Type == "edit_box");
        }

        var f2 = @"C:\HS-STEEL\allplot\hjhvlx\base.dcl";
        if (File.Exists(f2))
        {
            var p = DclParser.Parse(DclParser.Decode(File.ReadAllBytes(f2)));
            Assert.Contains(p.Definitions, d => d.Name == "ok_cancel");
            Assert.True(p.Definitions.Count > 30);
        }
    }
}
