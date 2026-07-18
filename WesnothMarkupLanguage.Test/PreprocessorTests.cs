using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WesnothMarkupLanguage.Test
{
    public class PreprocessorTests
    {
        [Fact] public async Task Expands_parameterized_macros_and_conditionals()
        {
            const string input = "#define UNIT ID\n[unit]\nid={ID}\n[/unit]\n#enddef\n#ifdef ENABLED\n{UNIT Bob}\n#endif\n";
            var options = new WmlPreprocessorOptions(); options.Defines["ENABLED"] = "";
            var result = await WmlPreprocessor.ProcessAsync(input, options);
            Assert.False(result.HasErrors); Assert.Contains("id=Bob", result.Text); Assert.Single(result.Syntax.Document.Tags);
        }
        [Fact] public async Task Enforces_sandboxed_include_roots()
        {
            string root = Path.Combine(Path.GetTempPath(), "wml-test-" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try { var options = new WmlPreprocessorOptions { SourceResolver = new FileSystemWmlSourceResolver(root) }; var result = await WmlPreprocessor.ProcessAsync("{../secret.cfg}\n", options, Path.Combine(root, "main.cfg")); Assert.True(result.HasErrors); }
            finally { Directory.Delete(root, true); }
        }
        [Fact] public async Task Honors_version_conditionals()
        {
            var result = await WmlPreprocessor.ProcessAsync("#ifver WESNOTH_VERSION >= 1.18.0\n[a]\n[/a]\n#else\n[b]\n[/b]\n#endif\n"); Assert.Equal("a", Assert.Single(result.Syntax.Document.Tags).Name);
        }
        [Fact] public async Task Supports_optional_named_arguments()
        {
            const string input = "#define ITEM ID\n#arg COST\n10\n#endarg\n[item]\nid={ID}\ncost={COST}\n[/item]\n#enddef\n{ITEM sword (COST=20)}\n";
            var result = await WmlPreprocessor.ProcessAsync(input); Assert.False(result.HasErrors); var item = Assert.Single(result.Syntax.Document.Tags); Assert.Equal("20", item.GetAttribute("cost"));
        }
        [Fact] public async Task Resolves_optional_defaults_that_reference_other_parameters()
        {
            const string input = "#define ITEM ID\n#arg BASE\n{ID}-base#endarg\n#arg IMAGE\n{BASE}#endarg\n[item]\nid={ID}\nimage={IMAGE}\n[/item]\n#enddef\n{ITEM sword}\n{ITEM axe BASE=custom}\n";
            var resolver = new RecordingResolver(); var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync(input, options);
            Assert.False(result.HasErrors); Assert.Equal(new[] { "sword-base", "custom" }, result.Syntax.Document.Tags.Select(tag => tag.GetAttribute("image")).ToArray());
            Assert.Empty(resolver.Requests); Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "WML2011" || diagnostic.Code == "WML2014");
        }
        [Fact] public async Task Supports_inline_endarg_and_ungrouped_named_optional_arguments()
        {
            const string input = "#define GRAPHIC BASE\n#arg IPF\nNOP#endarg\n#arg ARG\n#endarg\n[frame]\nimage=\"{BASE}~{IPF}({ARG})\"\n[/frame]\n#enddef\n{GRAPHIC zombie IPF=CS ARG=10,-12,-77}\n";
            var resolver = new RecordingResolver(); var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync(input, options);
            Assert.False(result.HasErrors); Assert.Equal("zombie~CS(10,-12,-77)", Assert.Single(result.Syntax.Document.Tags).GetAttribute("image"));
            Assert.Empty(resolver.Requests); Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "WML2011" || diagnostic.Code == "WML2014");
        }
        [Fact] public async Task Detects_include_cycles()
        {
            string root = Path.Combine(Path.GetTempPath(), "wml-cycle-" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "a.cfg"), "{b.cfg}\n"); File.WriteAllText(Path.Combine(root, "b.cfg"), "{a.cfg}\n");
            try { var options = new WmlPreprocessorOptions { SourceResolver = new FileSystemWmlSourceResolver(root) }; var result = await WmlPreprocessor.ProcessAsync("{a.cfg}\n", options, Path.Combine(root, "main.cfg")); Assert.Contains(result.Diagnostics, d => d.Code == "WML2013"); }
            finally { Directory.Delete(root, true); }
        }
        [Fact] public async Task Existing_root_candidate_wins_when_relative_candidate_is_missing()
        {
            string root = Path.Combine(Path.GetTempPath(), "wml-root-candidate-" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "core")); File.WriteAllText(Path.Combine(root, "core", "macros.cfg"), "[loaded]\n[/loaded]\n");
            try
            {
                var options = new WmlPreprocessorOptions { SourceResolver = new FileSystemWmlSourceResolver(root) };
                var result = await WmlPreprocessor.ProcessAsync("{core/macros.cfg}\n", options, Path.Combine(root, "core", "_main.cfg"));
                Assert.False(result.HasErrors); Assert.Equal("loaded", Assert.Single(result.Syntax.Document.Tags).Name);
            }
            finally { Directory.Delete(root, true); }
        }
        [Fact] public async Task Directory_includes_resolve_nested_relative_includes_from_that_directory()
        {
            string root = Path.Combine(Path.GetTempPath(), "wml-directory-relative-" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "core", "encyclopedia"));
            File.WriteAllText(Path.Combine(root, "core", "encyclopedia", "_main.cfg"), "{./calendar.cfg}\n");
            File.WriteAllText(Path.Combine(root, "core", "encyclopedia", "calendar.cfg"), "[calendar]\n[/calendar]\n");
            try
            {
                var options = new WmlPreprocessorOptions { SourceResolver = new FileSystemWmlSourceResolver(root) };
                var result = await WmlPreprocessor.ProcessAsync("{core/encyclopedia/}\n", options, Path.Combine(root, "main.cfg"));
                Assert.False(result.HasErrors); Assert.Equal("calendar", Assert.Single(result.Syntax.Document.Tags).Name);
            }
            finally { Directory.Delete(root, true); }
        }
        [Fact] public async Task Directory_include_segments_preserve_child_file_context_for_relative_includes()
        {
            string root = Path.Combine(Path.GetTempPath(), "wml-directory-segment-" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "campaign", "scenarios"));
            File.WriteAllText(Path.Combine(root, "campaign", "scenarios", "_main.cfg"), "{./child.cfg}\n");
            File.WriteAllText(Path.Combine(root, "campaign", "scenarios", "child.cfg"), "[scenario]\nid=child\n[/scenario]\n");
            try
            {
                var options = new WmlPreprocessorOptions { SourceResolver = new FileSystemWmlSourceResolver(root) };
                var result = await WmlPreprocessor.ProcessAsync("{campaign/}\n", options, Path.Combine(root, "main.cfg"));
                var scenario = Assert.Single(result.Syntax.Document.Tags);
                Assert.False(result.HasErrors);
                Assert.Equal("child", scenario.GetAttribute("id"));
                Assert.Equal(Path.Combine(root, "campaign", "scenarios", "child.cfg"), scenario.Provenance?.LogicalSource);
            }
            finally { Directory.Delete(root, true); }
        }
        [Fact] public async Task Enforces_output_limit()
        {
            var options = new WmlPreprocessorOptions { MaxOutputBytes = 2 }; await Assert.ThrowsAsync<WmlException>(() => WmlPreprocessor.ProcessAsync("[a]\n[/a]\n", options));
        }
        [Fact] public async Task Does_not_expand_macros_in_comments()
        {
            var result = await WmlPreprocessor.ProcessAsync("#define X\n[a]\n[/a]\n#enddef\n# {X}\n[b] # {X}\n[/b]\n"); Assert.Single(result.Syntax.Document.Tags); Assert.Equal("b", result.Syntax.Document.Tags.Single().Name);
        }

        [Theory]
        [InlineData("#define INLINE VALUE\n{VALUE}#enddef\nvalue=before-{INLINE after}-end\n")]
        [InlineData("#ifdef NORMAL\n#define INLINE VALUE\n{VALUE}#enddef\n#endif\nvalue=before-{INLINE after}-end\n")]
        public async Task Recognizes_inline_enddef_without_inserting_a_newline(string input)
        {
            var options = new WmlPreprocessorOptions(); options.Defines["NORMAL"] = "";
            var result = await WmlPreprocessor.ProcessAsync(input, options);
            Assert.False(result.HasErrors); Assert.Equal("value=before-after-end\n", result.Text);
            Assert.Contains("INLINE", result.Macros.Keys); Assert.DoesNotContain(result.Diagnostics, d => d.Code == "WML2004" || d.Code == "WML2008");
        }

        [Theory]
        [InlineData("#ifndef EASY\n[a][/a] #endif\n[b][/b]\n", new[] { "a", "b" })]
        [InlineData("#ifdef EASY\n[a][/a] #endif\n[b][/b]\n", new[] { "b" })]
        [InlineData("#ifdef EASY\n[a][/a] #else\n[b][/b] #endif\n", new[] { "b" })]
        public async Task Recognizes_inline_conditional_directives_after_content(string input, string[] expectedTags)
        {
            var result = await WmlPreprocessor.ProcessAsync(input);
            Assert.False(result.HasErrors); Assert.Equal(expectedTags, result.Syntax.Document.Tags.Select(tag => tag.Name).ToArray());
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "WML2003" || d.Code == "WML2004" || d.Code == "WML2008");
        }

        [Fact] public async Task Does_not_treat_hash_comment_runs_as_inline_conditionals()
        {
            const string input = "[scenario]\n    ######endif\n    [event][/event] ####else\n[/scenario]\n";
            var result = await WmlPreprocessor.ProcessAsync(input);
            Assert.False(result.HasErrors); Assert.DoesNotContain(result.Diagnostics, d => d.Code == "WML2003" || d.Code == "WML2004");
            Assert.Equal("scenario", Assert.Single(result.Syntax.Document.Tags).Name);
        }

        [Fact] public async Task Preserves_hashes_and_directive_text_inside_multiline_quoted_values()
        {
            const string input = "[trait]\ndescription= _ \"<span color='#00FF00'>positive</span>\n<span color='#FF0000'>negative</span>\n#endif is literal\"\n[/trait]\n";
            var result = await WmlPreprocessor.ProcessAsync(input);
            Assert.False(result.HasErrors); Assert.Contains("#FF0000", result.Text); Assert.Contains("#endif is literal", result.Text);
            Assert.Equal("<span color='#00FF00'>positive</span>\n<span color='#FF0000'>negative</span>\n#endif is literal", Assert.Single(result.Syntax.Document.Tags).GetAttribute("description"));
        }

        [Fact] public async Task Multiline_brace_continuations_update_quoted_state_before_later_directives()
        {
            const string input = "#define VARIABLE NAME VALUE\n[set]\nname={NAME}\nvalue={VALUE}\n[/set]\n#enddef\n{VARIABLE message \"first line\nsecond line\"}\n#define AFTER\n[after]\n[/after]\n#enddef\n{AFTER}\n";
            var result = await WmlPreprocessor.ProcessAsync(input);
            Assert.False(result.HasErrors); Assert.DoesNotContain("#define AFTER", result.Text);
            Assert.Equal(new[] { "set", "after" }, result.Syntax.Document.Tags.Select(tag => tag.Name).ToArray());
            Assert.Equal("first line\nsecond line", result.Syntax.Document.Tags.First().GetAttribute("value"));
        }

        [Fact] public async Task Does_not_recognize_enddef_inside_quoted_or_strong_quoted_macro_text()
        {
            const string input = "#define TEXT\nvalue=\"first\n#enddef inside quote\"\nraw=<<#enddef inside raw>>\nactual#enddef\n";
            var result = await WmlPreprocessor.ProcessAsync(input);
            Assert.False(result.HasErrors); string body = result.Macros["TEXT"].Body; Assert.Contains("#enddef inside quote", body); Assert.Contains("<<#enddef inside raw>>", body); Assert.EndsWith("actual", body);
        }

        [Fact] public async Task Expands_calls_in_quoted_values_and_formula_strings()
        {
            const string input = "#define WORD VALUE\n{VALUE}#enddef\nname=\"prefix-{WORD middle}-suffix\"\nformula=\"$(1 + {WORD 2})\"\n";
            var result = await WmlPreprocessor.ProcessAsync(input);
            Assert.False(result.HasErrors); Assert.Contains("name=\"prefix-middle-suffix\"", result.Text); Assert.Contains("formula=\"$(1 + 2)\"", result.Text);
        }

        [Fact] public async Task Expands_quoted_arguments_and_nested_brace_calls()
        {
            const string input = "#define ID VALUE\n{VALUE}#enddef\n#define WRAP VALUE\n{ID {VALUE}}#enddef\nvalue={WRAP \"quoted value\"}\n";
            var result = await WmlPreprocessor.ProcessAsync(input);
            Assert.False(result.HasErrors); Assert.Equal("value=\"quoted value\"\n", result.Text);
        }

        [Fact] public async Task Preserves_calls_inside_strong_quoted_values()
        {
            const string input = "#define WORD VALUE\n{VALUE}#enddef\nvalue=<<prefix-{WORD middle}-suffix>>\n";
            var result = await WmlPreprocessor.ProcessAsync(input);
            Assert.False(result.HasErrors); Assert.Contains("<<prefix-{WORD middle}-suffix>>", result.Text);
        }

        [Fact] public async Task Preserves_multiline_strong_quoted_braces_and_resumes_expansion_after_closing()
        {
            const string literal = "<<\nlocal probe = { id = \"probe\", location = { x = 1 } }\nformula = $(items[{index}])\n#not-a-directive {ignored value}\n>>";
            const string input = "#define WORD VALUE\n{VALUE}#enddef\n[lua]\ncode=" + literal + " + \"-{WORD expanded}\"\n[/lua]\n";
            var resolver = new RecordingResolver(); var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync(input, options, "fixture.cfg");
            Assert.False(result.HasErrors); Assert.Contains(literal + " + \"-expanded\"", result.Text); Assert.Empty(resolver.Requests);
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "WML2009" || d.Code == "WML2011" || d.Code == "WML2012" || d.Code == "WML2014");
            int brace = result.Text.IndexOf("{ id =", System.StringComparison.Ordinal); Assert.True(brace >= 0);
            Assert.Contains(result.SourceMap, entry => entry.Source == "fixture.cfg" && entry.OutputStart <= brace && entry.OutputStart + entry.OutputLength > brace);
        }

        [Fact] public async Task Expands_registered_macros_passed_as_nested_arguments()
        {
            const string input = "#define INNER VALUE\n{VALUE}#enddef\n#define OUTER VALUE\n[scenario]\nturns={VALUE}\n[/scenario]\n#enddef\n{OUTER {INNER 2}}\n";
            var result = await WmlPreprocessor.ProcessAsync(input);
            Assert.False(result.HasErrors); Assert.Equal("2", Assert.Single(result.Syntax.Document.Tags).GetAttribute("turns")); Assert.DoesNotContain(result.Diagnostics, d => d.Code == "WML2009");
        }

        [Theory]
        [InlineData("{OUTER {MIDDLE {INNER 2}}}", "2")]
        [InlineData("{OUTER {INNER \"two words\"}}", "two words")]
        [InlineData("{OUTER (VALUE={INNER 2})}", "2")]
        [InlineData("{OUTER\n {INNER 2}\n}", "2")]
        public async Task Retains_nested_macro_argument_boundaries(string invocation, string expected)
        {
            const string definitions = "#define INNER VALUE\n{VALUE}#enddef\n#define MIDDLE VALUE\n{VALUE}#enddef\n#define OUTER VALUE\nvalue={VALUE}\n#enddef\n";
            var result = await WmlPreprocessor.ProcessAsync(definitions + invocation + "\n");
            Assert.False(result.HasErrors); Assert.Equal("value=" + (invocation.Contains("\"") ? "\"" + expected + "\"" : expected) + "\n\n", result.Text);
        }

        [Fact] public async Task Preserves_nested_macro_source_map_provenance_and_offsets()
        {
            var resolver = new RecordingResolver(new Dictionary<string, WmlSource>
            {
                ["inner.cfg"] = new WmlSource("inner.cfg", "#define INNER VALUE\n{VALUE}#enddef\n"),
                ["outer.cfg"] = new WmlSource("outer.cfg", "#define OUTER VALUE\n{VALUE}#enddef\n")
            });
            var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("{inner.cfg}\n{outer.cfg}\nvalue=prefix-{OUTER {INNER 2}}-suffix\n", options, "main.cfg");
            int insertion = result.Text.IndexOf("2", System.StringComparison.Ordinal);
            Assert.False(result.HasErrors); Assert.True(insertion >= 0); Assert.Equal(new[] { "inner.cfg", "outer.cfg" }, resolver.Requests);
            Assert.Contains(result.SourceMap, entry => entry.Source == "inner.cfg" && entry.OutputStart == insertion && entry.OutputLength == 1);
        }

        [Fact] public async Task Consumes_positional_grouping_parentheses_around_a_tag_block()
        {
            const string input = "#define WRAP WML\n{WML}\n#enddef\n{WRAP (\n[tag]\nid=x\n[/tag]\n)}\n";
            var resolver = new RecordingResolver(); var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync(input, options);
            var tag = Assert.Single(result.Syntax.Document.Tags); Assert.False(result.HasErrors); Assert.Equal("tag", tag.Name); Assert.Equal("x", tag.GetAttribute("id")); Assert.Empty(resolver.Requests);
            Assert.DoesNotContain(result.Syntax.Diagnostics, d => d.Code == "WML1007"); Assert.DoesNotContain("(\n[tag]", result.Text); Assert.DoesNotContain("[/tag]\n)", result.Text);
        }

        [Theory]
        [InlineData("((text))", "(text)")]
        [InlineData("(\"two words\")", "\"two words\"")]
        [InlineData("({INNER 2})", "2")]
        [InlineData("(<<line 1\nline 2>>)", "<<line 1\nline 2>>")]
        public async Task Unwraps_one_balanced_positional_group_and_expands_its_body(string argument, string expected)
        {
            const string definitions = "#define INNER VALUE\n{VALUE}#enddef\n#define WRAP WML\nvalue={WML}\n#enddef\n";
            var result = await WmlPreprocessor.ProcessAsync(definitions + "{WRAP " + argument + "}\n");
            Assert.False(result.HasErrors); Assert.Contains("value=" + expected, result.Text); Assert.DoesNotContain(result.Syntax.Diagnostics, d => d.Code == "WML1007");
        }

        [Fact] public async Task Keeps_named_groups_and_grouped_source_map_provenance()
        {
            var resolver = new RecordingResolver(new Dictionary<string, WmlSource>
            {
                ["macros.cfg"] = new WmlSource("macros.cfg", "#define WRAP WML\n{WML}\n#enddef\n")
            });
            var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("{macros.cfg}\n{WRAP (WML=[tag]\nid=x\n[/tag])}\n", options, "main.cfg");
            int insertion = result.Text.IndexOf("id=x", System.StringComparison.Ordinal);
            Assert.False(result.HasErrors); Assert.True(insertion >= 0); Assert.Equal(new[] { "macros.cfg" }, resolver.Requests);
            Assert.Contains(result.SourceMap, entry => entry.Source == "macros.cfg" && entry.OutputStart <= insertion && entry.OutputStart + entry.OutputLength >= insertion + 4);
        }

        [Fact] public async Task Binds_unknown_grouped_attribute_assignment_positionally_without_shifting()
        {
            const string input = "#define WRAP FILTER VALUE\n[event]\n[filter]\n{FILTER}\n[/filter]\nvalue={VALUE}\n[/event]\n#enddef\n{WRAP (id=probe) 7}\n";
            var result = await WmlPreprocessor.ProcessAsync(input); var action = Assert.Single(result.Syntax.Document.Tags); var filter = Assert.Single(action.Tags);
            Assert.False(result.HasErrors); Assert.Equal("probe", filter.GetAttribute("id")); Assert.Equal("7", action.GetAttribute("value")); Assert.DoesNotContain(result.Syntax.Diagnostics, d => d.Code == "WML1007");
        }

        [Fact] public async Task Splits_adjacent_grouped_positional_arguments_without_whitespace()
        {
            const string input = "#define WRAP FIRST SECOND VALUE EXTRA\n[event]\n[filter]\n{FIRST}\n[/filter]\n[filter_second]\n{SECOND}\n[/filter_second]\nvalue={VALUE}\n[extra]\n{EXTRA}\n[/extra]\n[/event]\n#enddef\n{WRAP (\nid=one\n)(id=two) 0 (\n)}\n";
            var result = await WmlPreprocessor.ProcessAsync(input); var action = Assert.Single(result.Syntax.Document.Tags);
            Assert.False(result.HasErrors); Assert.Equal("one", action.Tags.ElementAt(0).GetAttribute("id")); Assert.Equal("two", action.Tags.ElementAt(1).GetAttribute("id")); Assert.Equal("0", action.GetAttribute("value"));
            Assert.DoesNotContain(result.Syntax.Diagnostics, d => d.Code == "WML1007"); Assert.DoesNotContain(")(id=two)", result.Text);
        }

        [Fact] public async Task Named_formals_and_optional_arguments_do_not_shift_positional_binding()
        {
            const string definitions = "#define BIND FIRST SECOND\n#arg OPTIONAL\ndefault\n#endarg\n[result]\nfirst={FIRST}\nsecond={SECOND}\noptional={OPTIONAL}\n[/result]\n#enddef\n";
            var namedFirst = await WmlPreprocessor.ProcessAsync(definitions + "{BIND (SECOND=two) one (OPTIONAL=custom)}\n"); var first = Assert.Single(namedFirst.Syntax.Document.Tags);
            Assert.False(namedFirst.HasErrors); Assert.Equal("one", first.GetAttribute("first")); Assert.Equal("two", first.GetAttribute("second")); Assert.Equal("custom", first.GetAttribute("optional"));
            var overrideEarlier = await WmlPreprocessor.ProcessAsync(definitions + "{BIND one (FIRST=override) two}\n"); var second = Assert.Single(overrideEarlier.Syntax.Document.Tags);
            Assert.False(overrideEarlier.HasErrors); Assert.Equal("override", second.GetAttribute("first")); Assert.Equal("two", second.GetAttribute("second"));
            var ungrouped = await WmlPreprocessor.ProcessAsync(definitions + "{BIND SECOND=two one OPTIONAL=custom}\n"); var third = Assert.Single(ungrouped.Syntax.Document.Tags);
            Assert.False(ungrouped.HasErrors); Assert.Equal("one", third.GetAttribute("first")); Assert.Equal("two", third.GetAttribute("second")); Assert.Equal("custom", third.GetAttribute("optional"));
        }

        [Fact] public async Task Preserves_multiline_nested_and_quoted_equals_in_positional_groups()
        {
            const string input = "#define INNER VALUE\n{VALUE}#enddef\n#define WRAP FILTER VALUE\n[event]\n[filter]\n{FILTER}\n[/filter]\nvalue={VALUE}\n[/event]\n#enddef\n{WRAP (text=\"{INNER a=b}\"\nkind=probe) 7}\n";
            var result = await WmlPreprocessor.ProcessAsync(input); var action = Assert.Single(result.Syntax.Document.Tags); var filter = Assert.Single(action.Tags);
            Assert.False(result.HasErrors); Assert.Equal("a=b", filter.GetAttribute("text")); Assert.Equal("probe", filter.GetAttribute("kind")); Assert.Equal("7", action.GetAttribute("value"));
        }

        [Fact] public async Task Grouped_positional_substitution_retains_definition_and_invocation_provenance()
        {
            var resolver = new RecordingResolver(new Dictionary<string, WmlSource>
            {
                ["macros.cfg"] = new WmlSource("macros.cfg", "#define WRAP FILTER VALUE\n[event]\n[filter]\n{FILTER}\n[/filter]\nvalue={VALUE}\n[/event]\n#enddef\n")
            });
            var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("{macros.cfg}\n{WRAP (id=probe) 7}\n", options, "main.cfg"); int insertion = result.Text.IndexOf("id=probe", System.StringComparison.Ordinal);
            Assert.False(result.HasErrors); Assert.True(insertion >= 0); Assert.Contains(result.SourceMap, entry => entry.Source == "macros.cfg" && entry.OutputStart <= insertion && entry.OutputStart + entry.OutputLength >= insertion + 8);
            Assert.Contains(result.SourceMap, entry => entry.Source == "main.cfg" && entry.OutputStart <= insertion && entry.OutputStart + entry.OutputLength >= insertion + 8);
        }

        [Fact] public async Task Shifts_embedded_macro_source_map_entries_to_their_output_offsets()
        {
            var resolver = new RecordingResolver(new Dictionary<string, WmlSource>
            {
                ["macros.cfg"] = new WmlSource("macros.cfg", "#define WORD VALUE\n{VALUE}#enddef\n")
            });
            var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("{macros.cfg}\nvalue=prefix-{WORD middle}-suffix\n", options, "main.cfg");
            int insertion = result.Text.IndexOf("middle", System.StringComparison.Ordinal);
            Assert.True(insertion >= 0); Assert.Contains(result.SourceMap, entry => entry.Source == "macros.cfg" && entry.OutputStart == insertion && entry.OutputLength == "middle".Length);
        }

        [Fact] public async Task Registered_macros_bypass_the_source_resolver()
        {
            var resolver = new RecordingResolver(); var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("#define KNOWN VALUE\n{VALUE}#enddef\nvalue={KNOWN yes}\n", options);
            Assert.False(result.HasErrors); Assert.Empty(resolver.Requests);
        }

        [Fact] public async Task Unknown_argument_bearing_macro_reports_WML2014_with_context()
        {
            var resolver = new RecordingResolver(); var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("{WC_MISSING_MACRO one two}\n", options, "main.cfg");
            var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "WML2014"); var context = Assert.IsType<WmlPreprocessorDiagnosticContext>(diagnostic.PreprocessorContext);
            Assert.Equal("WC_MISSING_MACRO one two", context.Expression); Assert.Equal("WC_MISSING_MACRO", context.Symbol); Assert.Equal(WmlPreprocessorExpressionKind.MacroInvocation, context.ExpressionKind);
            Assert.False(context.MacroWasRegistered); Assert.True(context.IncludeFallbackAttempted); Assert.Equal("WC_MISSING_MACRO one two", context.IncludeCandidate);
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "WML2011" || d.Code == "WML2012"); Assert.Single(resolver.Requests);
        }

        [Fact] public async Task Unknown_macro_without_a_resolver_reports_that_fallback_did_not_run()
        {
            var result = await WmlPreprocessor.ProcessAsync("{MISSING argument}\n");
            var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "WML2014"); var context = Assert.IsType<WmlPreprocessorDiagnosticContext>(diagnostic.PreprocessorContext);
            Assert.False(context.IncludeFallbackAttempted); Assert.Null(context.IncludeCandidate); Assert.Contains("did not run", diagnostic.Message);
        }

        [Fact] public async Task Genuine_missing_include_retains_include_diagnostic_and_context()
        {
            var resolver = new RecordingResolver(); var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("{missing/file.cfg}\n", options, "main.cfg");
            var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "WML2012"); var context = Assert.IsType<WmlPreprocessorDiagnosticContext>(diagnostic.PreprocessorContext);
            Assert.Equal(WmlPreprocessorExpressionKind.Include, context.ExpressionKind); Assert.True(context.IncludeFallbackAttempted); Assert.Equal("missing/file.cfg", context.IncludeCandidate);
        }

        [Fact] public async Task Macro_expanded_dom_nodes_expose_definition_and_invocation_provenance()
        {
            var resolver = new RecordingResolver(new Dictionary<string, WmlSource>
            {
                ["macros.cfg"] = new WmlSource("macros.cfg", "#define UNIT ID\n[unit]\nid={ID}\n[/unit]\n#enddef\n")
            });
            var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("{macros.cfg}\n[event]\n{UNIT Bob}\n[/event]\n", options, "main.cfg");
            var unit = Assert.Single(Assert.Single(result.Syntax.Document.Tags).Tags);
            var provenance = Assert.IsType<WmlExpansionProvenance>(unit.Provenance);
            var frame = Assert.Single(provenance.ExpansionChain);
            Assert.Equal("macros.cfg", provenance.LogicalSource);
            Assert.Equal(WmlSourceReferencePrecision.Exact, provenance.Source.Precision);
            Assert.Equal("UNIT", frame.MacroSymbol);
            Assert.Equal("macros.cfg", frame.Definition.Source);
            Assert.Equal(WmlSourceReferencePrecision.Exact, frame.Definition.Precision);
            Assert.Equal("main.cfg", frame.Invocation.Source);
            Assert.Equal(WmlSourceReferencePrecision.Exact, frame.Invocation.Precision);
            Assert.Equal(3, frame.Invocation.Line);
            Assert.Equal(1, frame.Invocation.Column);
        }

        [Fact] public async Task Parser_diagnostics_from_macro_output_expose_expansion_provenance()
        {
            var resolver = new RecordingResolver(new Dictionary<string, WmlSource>
            {
                ["macros.cfg"] = new WmlSource("macros.cfg", "#define BAD\n)\n#enddef\n")
            });
            var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("{macros.cfg}\n{BAD}\n", options, "main.cfg");
            var diagnostic = Assert.Single(result.Syntax.Diagnostics, d => d.Code == "WML1007");
            var frame = Assert.Single(Assert.IsType<WmlExpansionProvenance>(diagnostic.Provenance).ExpansionChain);
            Assert.Equal("BAD", frame.MacroSymbol);
            Assert.Equal("macros.cfg", frame.Definition.Source);
            Assert.Equal("main.cfg", frame.Invocation.Source);
        }

        [Fact] public async Task Preprocessor_diagnostics_use_exact_expression_span_and_provenance_context()
        {
            var resolver = new RecordingResolver(); var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("prefix\n  {MISSING one two}\n", options, "main.cfg");
            var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "WML2014");
            Assert.Equal("main.cfg", diagnostic.Span.Source);
            Assert.Equal(2, diagnostic.Span.Line);
            Assert.Equal(3, diagnostic.Span.Column);
            Assert.Equal("{MISSING one two}".Length, diagnostic.Span.Length);
            var provenance = Assert.IsType<WmlExpansionProvenance>(diagnostic.Provenance);
            Assert.Equal(WmlSourceReferencePrecision.Exact, provenance.Source.Precision);
            Assert.Empty(provenance.ExpansionChain);
        }

        [Fact] public async Task Nested_macro_output_keeps_the_full_expansion_chain()
        {
            var resolver = new RecordingResolver(new Dictionary<string, WmlSource>
            {
                ["macros.cfg"] = new WmlSource("macros.cfg", "#define INNER VALUE\n{VALUE}#enddef\n#define OUTER VALUE\n[unit]\nid={INNER {VALUE}}\n[/unit]\n#enddef\n")
            });
            var options = new WmlPreprocessorOptions { SourceResolver = resolver };
            var result = await WmlPreprocessor.ProcessAsync("{macros.cfg}\n{OUTER Bob}\n", options, "main.cfg");
            var attribute = Assert.Single(Assert.Single(result.Syntax.Document.Tags).Attributes);
            var provenance = Assert.IsType<WmlExpansionProvenance>(attribute.Provenance);
            Assert.Equal(new[] { "OUTER", "INNER" }, provenance.ExpansionChain.Select(frame => frame.MacroSymbol).ToArray());
            Assert.Equal("main.cfg", provenance.ExpansionChain[0].Invocation.Source);
            Assert.Equal("macros.cfg", provenance.ExpansionChain[1].Invocation.Source);
        }

        private sealed class RecordingResolver : IWmlSourceResolver
        {
            private readonly IReadOnlyDictionary<string, WmlSource> _sources;
            public RecordingResolver(IReadOnlyDictionary<string, WmlSource>? sources = null) { _sources = sources ?? new Dictionary<string, WmlSource>(); }
            public IList<string> Requests { get; } = new List<string>();
            public Task<WmlSource?> ResolveAsync(string path, string? includingSource, CancellationToken cancellationToken) { Requests.Add(path); _sources.TryGetValue(path, out var source); return Task.FromResult(source); }
            public Task<bool> ExistsAsync(string path, string? includingSource, CancellationToken cancellationToken) => Task.FromResult(_sources.ContainsKey(path));
        }
    }
}
