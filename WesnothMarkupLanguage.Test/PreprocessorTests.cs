using System.IO;
using System.Linq;
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
    }
}
