using System.Linq;
using Xunit;

namespace WesnothMarkupLanguage.Test
{
    public class ParserTests
    {
        private const string Sample = "#textdomain wesnoth-test\r\n[unit_type]\r\n    id=Elvish_Fighter\r\n    name= _\"Fighter\" + \"!\" # display\r\n    description=<<line 1\r\nline 2>>\r\n    [attack]\r\n        damage,number=5,4\r\n    [/attack]\r\n[/unit_type]\r\n";

        [Fact] public void Lossless_round_trip_is_exact() { var tree = WmlParser.Parse(Sample, "unit.cfg"); Assert.False(tree.HasErrors); Assert.Equal(Sample, WmlWriter.Write(tree)); }
        [Fact] public void Editing_ordered_collection_invalidates_lossless_source() { var tree = WmlParser.Parse("[a]\n[/a]\n"); tree.Document.Children.Add(new WmlTag("b")); var output = WmlWriter.Write(tree); Assert.Contains("[b]", output); }
        [Fact] public void Builds_semantic_tree_and_multiple_assignments() { var tag = WmlParser.Parse(Sample).Document.Tags.Single(); Assert.Equal("Elvish_Fighter", tag.GetAttribute("id")); var attack = tag.Tags.Single(); Assert.Equal("5", attack.GetAttribute("damage")); Assert.Equal("4", attack.GetAttribute("number")); }
        [Fact] public void Applies_tag_amendment() { var doc = WmlParser.Parse("[a]\nx=1\n[/a]\n[+a]\nx=2\ny=3\n[/a]\n").Document; var tag = doc.Tags.Single(); Assert.Equal("2", tag.GetAttribute("x")); Assert.Equal("3", tag.GetAttribute("y")); }
        [Fact] public void Reports_mismatched_tags() { var tree = WmlParser.Parse("[a]\n[/b]\n"); Assert.True(tree.HasErrors); Assert.Contains(tree.Diagnostics, d => d.Code == "WML1004"); }
        [Fact] public void Parses_special_values() { var value = WmlValue.Parse("_\"Hello\" + $unit.name + $(1 + 2)"); Assert.Equal(3, value.Components.Count); Assert.Equal(WmlValueComponentKind.Translatable, value.Components[0].Kind); Assert.Equal(WmlValueComponentKind.Variable, value.Components[1].Kind); Assert.Equal(WmlValueComponentKind.Formula, value.Components[2].Kind); }
    }
}
