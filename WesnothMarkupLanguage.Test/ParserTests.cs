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

        [Fact] public void Tokenizes_adjacent_tags_with_exact_opening_and_closing_spans()
        {
            const string input = "[outer][redraw][/redraw] [first][/first][second][/second] # trailing\n[/outer]\n";
            var tree = WmlParser.Parse(input, "adjacent.cfg"); var outer = Assert.Single(tree.Document.Tags); var children = outer.Tags.ToList();
            Assert.False(tree.HasErrors); Assert.Equal(new[] { "redraw", "first", "second" }, children.Select(t => t.Name)); Assert.Single(outer.Children.OfType<WmlComment>());
            Assert.Equal(input.IndexOf("[redraw]", System.StringComparison.Ordinal), children[0].Span!.Start); Assert.Equal("[redraw]".Length, children[0].Span!.Length);
            Assert.Equal(input.IndexOf("[/redraw]", System.StringComparison.Ordinal), children[0].ClosingSpan!.Start); Assert.Equal("[/redraw]".Length, children[0].ClosingSpan!.Length);
            Assert.Equal(input.LastIndexOf("[/outer]", System.StringComparison.Ordinal), outer.ClosingSpan!.Start); Assert.Equal(input, WmlWriter.Write(tree));
        }

        [Theory]
        [InlineData("[outer][inner][/inner][/outer]", 1)]
        [InlineData("[a][/a][b][/b]", 2)]
        [InlineData("[a][/a][+a][/a]", 1)]
        public void Preserves_tag_stack_for_same_line_nesting_siblings_and_amendments(string input, int roots)
        {
            var tree = WmlParser.Parse(input); Assert.False(tree.HasErrors); Assert.Equal(roots, tree.Document.Tags.Count()); Assert.All(tree.Document.FindTags("inner"), tag => Assert.NotNull(tag.ClosingSpan));
        }

        [Fact] public void Same_line_mismatch_uses_the_individual_closing_token_span()
        {
            const string input = "[outer][inner][/outer]"; var tree = WmlParser.Parse(input, "mismatch.cfg"); var diagnostic = Assert.Single(tree.Diagnostics, d => d.Code == "WML1004");
            Assert.Equal(input.IndexOf("[/outer]", System.StringComparison.Ordinal), diagnostic.Span.Start); Assert.Equal("[/outer]".Length, diagnostic.Span.Length);
            Assert.Null(Assert.Single(tree.Document.Tags).ClosingSpan); Assert.Null(new WmlTag("canonical").ClosingSpan);
        }

        [Fact] public void Parses_multiline_concatenated_attribute_components_and_span()
        {
            const string input = "[message]\n    message= _ \"first\" + # translator note\n        _ \"second\" +\n        \"!\" + $unit.name +\n        $(1 + 2)\n[/message]\n";
            var tree = WmlParser.Parse(input, "continuation.cfg"); var attribute = Assert.Single(Assert.Single(tree.Document.Tags).Attributes);
            Assert.False(tree.HasErrors); Assert.Equal("firstsecond!$unit.name$(1 + 2)", attribute.Value.Text); Assert.Equal(5, attribute.Value.Components.Count);
            int start = input.IndexOf("message=", System.StringComparison.Ordinal), end = input.IndexOf("\n[/message]", System.StringComparison.Ordinal);
            Assert.Equal(start, attribute.Span!.Start); Assert.Equal(end - start, attribute.Span!.Length); Assert.Equal(2, attribute.Span!.Line); Assert.Equal(5, attribute.Span!.Column);
            Assert.Equal(input, WmlWriter.Write(tree));
        }

        [Fact] public void Parses_multiple_untranslated_continuations_with_optional_whitespace_and_comments()
        {
            const string input = "value=\"a\"+\n \"b\" +   # keep reading\n $suffix\n";
            var tree = WmlParser.Parse(input); var attribute = Assert.Single(tree.Document.Attributes);
            Assert.False(tree.HasErrors); Assert.Equal("ab$suffix", attribute.Value.Text); Assert.Equal(3, attribute.Value.Components.Count);
        }

        [Fact] public void First_additive_tag_creates_an_ordinary_base_with_lossless_spans_and_children()
        {
            const string input = "[unit][+modifications] # created from additive\n[object]\nid=probe\n[/object][/modifications][/unit]\n";
            var tree = WmlParser.Parse(input, "additive.cfg"); var unit = Assert.Single(tree.Document.Tags); var modifications = Assert.Single(unit.Tags); var item = Assert.Single(modifications.Tags);
            Assert.False(tree.HasErrors); Assert.Equal("modifications", modifications.Name); Assert.False(modifications.IsAmendment); Assert.Equal("probe", item.GetAttribute("id")); Assert.Single(modifications.Children.OfType<WmlComment>());
            Assert.Equal(input.IndexOf("[+modifications]", System.StringComparison.Ordinal), modifications.Span!.Start); Assert.Equal("[+modifications]".Length, modifications.Span!.Length);
            Assert.Equal(input.IndexOf("[/modifications]", System.StringComparison.Ordinal), modifications.ClosingSpan!.Start); Assert.DoesNotContain(tree.Diagnostics, d => d.Code == "WML1010");
            Assert.Equal(input, WmlWriter.Write(tree)); Assert.DoesNotContain("[+modifications]", WmlWriter.Write(tree.Document));
        }

        [Fact] public void Later_additive_tags_merge_into_the_created_or_existing_base()
        {
            const string input = "[unit]\n[+modifications]\nstate=first\n[one][/one]\n[/modifications]\n[+modifications]\nstate=second\nextra=yes\n[two][/two]\n# retained\n[/modifications]\n[/unit]\n";
            var tree = WmlParser.Parse(input); var modifications = Assert.Single(Assert.Single(tree.Document.Tags).Tags);
            Assert.False(tree.HasErrors); Assert.False(modifications.IsAmendment); Assert.Equal("second", modifications.GetAttribute("state")); Assert.Equal("yes", modifications.GetAttribute("extra"));
            Assert.Equal(new[] { "one", "two" }, modifications.Tags.Select(t => t.Name)); Assert.Single(modifications.Children.OfType<WmlComment>()); Assert.DoesNotContain(tree.Diagnostics, d => d.Code == "WML1010");
        }
    }
}
