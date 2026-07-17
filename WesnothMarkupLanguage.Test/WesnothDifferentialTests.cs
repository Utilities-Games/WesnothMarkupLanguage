using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using WesnothMarkupLanguage.Test.Integration;

namespace WesnothMarkupLanguage.Test
{
    /// <summary>Release CI provisions WESNOTH_1_18_7_EXECUTABLE to activate this compatibility oracle.</summary>
    public class WesnothDifferentialTests
    {
        [Fact]
        public async Task Output_matches_official_1_18_7_preprocessor_for_reference_fixture()
        {
            DotEnvTestConfiguration.EnsureLoaded();
            string? executable = Environment.GetEnvironmentVariable("WESNOTH_1_18_7_EXECUTABLE");
            if (string.IsNullOrWhiteSpace(executable)) return;
            const string input = "#define MAKE ID\n[unit]\nid=\"{ID}\"\n[/unit]\n#enddef\n#ifdef HARD\n{MAKE hard}\n#else\n{MAKE normal}\n#endif\n";
            string root = Path.Combine(Path.GetTempPath(), "wml-differential-" + Guid.NewGuid().ToString("N")); string output = Path.Combine(root, "output"); Directory.CreateDirectory(output); string source = Path.Combine(root, "fixture.cfg"); File.WriteAllText(source, input);
            try
            {
                var start = new ProcessStartInfo(executable!, $"--preprocess \"{source}\" \"{output}\" --preprocess-defines=HARD") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
                using var process = Process.Start(start)!; await process.WaitForExitAsync(); string stderr = await process.StandardError.ReadToEndAsync(); Assert.True(process.ExitCode == 0, stderr);
                string officialFile = Directory.GetFiles(output, "*.cfg").Single(); var oursOptions = new WmlPreprocessorOptions(); oursOptions.Defines["HARD"] = ""; var ours = await WmlPreprocessor.ProcessAsync(input, oursOptions, source);
                string officialCanonical = WmlWriter.Write(WmlParser.Parse(File.ReadAllText(officialFile)).Document); string oursCanonical = WmlWriter.Write(ours.Syntax.Document); Assert.Equal(officialCanonical, oursCanonical);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public async Task Inline_and_value_context_expansion_matches_official_1_18_7()
        {
            DotEnvTestConfiguration.EnsureLoaded();
            string? executable = Environment.GetEnvironmentVariable("WESNOTH_1_18_7_EXECUTABLE");
            if (string.IsNullOrWhiteSpace(executable)) return;
            const string input = "#define WORD VALUE\n{VALUE}#enddef\n#ifdef NORMAL\n#define CONDITIONAL VALUE\n{VALUE}#enddef\n#endif\n[fixture]\nargument={WORD \"quoted value\"}\nconditional={CONDITIONAL yes}\nformula=\"$(1 + {WORD 2})\"\nquoted=\"prefix-{WORD middle}-suffix\"\nstrong=<<prefix-{WORD literal}-suffix>>\n[/fixture]\n";
            string root = Path.Combine(Path.GetTempPath(), "wml-differential-context-" + Guid.NewGuid().ToString("N")); string output = Path.Combine(root, "output"); Directory.CreateDirectory(output); string source = Path.Combine(root, "fixture.cfg"); File.WriteAllText(source, input);
            try
            {
                var start = new ProcessStartInfo(executable!, $"--preprocess \"{source}\" \"{output}\" --preprocess-defines=NORMAL") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
                using var process = Process.Start(start)!; await process.WaitForExitAsync(); string stderr = await process.StandardError.ReadToEndAsync(); Assert.True(process.ExitCode == 0, stderr);
                string officialFile = Directory.GetFiles(output, "*.cfg").Single(); var oursOptions = new WmlPreprocessorOptions(); oursOptions.Defines["NORMAL"] = ""; var ours = await WmlPreprocessor.ProcessAsync(input, oursOptions, source);
                var officialTag = Assert.Single(WmlParser.Parse(File.ReadAllText(officialFile)).Document.Tags); var oursTag = Assert.Single(ours.Syntax.Document.Tags);
                foreach (string name in new[] { "argument", "conditional", "formula", "quoted", "strong" }) Assert.Equal(officialTag.GetAttribute(name), oursTag.GetAttribute(name));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
