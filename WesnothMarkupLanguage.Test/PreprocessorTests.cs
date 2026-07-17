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
        [Fact] public async Task Detects_include_cycles()
        {
            string root = Path.Combine(Path.GetTempPath(), "wml-cycle-" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "a.cfg"), "{b.cfg}\n"); File.WriteAllText(Path.Combine(root, "b.cfg"), "{a.cfg}\n");
            try { var options = new WmlPreprocessorOptions { SourceResolver = new FileSystemWmlSourceResolver(root) }; var result = await WmlPreprocessor.ProcessAsync("{a.cfg}\n", options, Path.Combine(root, "main.cfg")); Assert.Contains(result.Diagnostics, d => d.Code == "WML2013"); }
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
