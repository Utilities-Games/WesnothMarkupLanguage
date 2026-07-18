using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WesnothMarkupLanguage.CampaignValidator;
using Xunit;

namespace WesnothMarkupLanguage.Test
{
    public sealed class CampaignValidatorTests
    {
        [Fact]
        public async Task Selected_campaign_builds_full_core_report_and_activates_defines()
        {
            using var installation = SyntheticInstallation.Create();
            installation.WriteCore("{core/macros/}\n[core_marker]\nid=loaded\n[/core_marker]\n");
            installation.WriteFile(Path.Combine("data", "core", "macros", "_main.cfg"), "#define SYNTHETIC_CORE_MACRO\n[macro_marker]\n[/macro_marker]\n#enddef\n{SYNTHETIC_CORE_MACRO}\n");
            installation.WriteCampaign("Alpha", Campaign("Alpha", "CAMPAIGN_ALPHA", "#ifdef CAMPAIGN_ALPHA\n#ifdef NORMAL\n{./units/}\n{./scenarios/}\n#endif\n#endif\n"));
            installation.WriteFile(Path.Combine("data", "campaigns", "Alpha", "scenarios", "01_Alpha.cfg"), "[scenario]\nid=active\nname= _ \"Active\"\nnext_scenario=null\n[side]\nside=1\n[/side]\n[event]\nname=prestart\n[/event]\n[/scenario]\n");
            installation.WriteFile(Path.Combine("data", "campaigns", "Alpha", "units", "Alpha_Unit.cfg"), "[unit_type]\nid=Alpha Unit\nname= _ \"Alpha Unit\"\nrace=human\nlevel=1\nhitpoints=32\nmovement=5\n[attack]\nname=sword\n[/attack]\n[/unit_type]\n");
            string reportPath = Path.Combine(installation.Root, "reports", "report.json");

            int exitCode = await Run(installation.Root, reportPath, "--campaign", "alpha");

            Assert.Equal(0, exitCode);
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            JsonElement root = report.RootElement;
            Assert.Equal("1.2", root.GetProperty("schemaVersion").GetString());
            Assert.Equal("NORMAL", root.GetProperty("configuration").GetProperty("difficulty").GetString());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("passed").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("scenarioFiles").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("scenarioFilesPassed").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("scenarios").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("unitFiles").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("unitFilesPassed").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("unitTypes").GetInt32());
            JsonElement result = Assert.Single(root.GetProperty("campaigns").EnumerateArray());
            Assert.Equal("Alpha", result.GetProperty("name").GetString());
            Assert.Equal("Alpha", result.GetProperty("campaignId").GetString());
            Assert.Equal("CAMPAIGN_ALPHA", result.GetProperty("campaignDefine").GetString());
            Assert.Equal("Passed", result.GetProperty("status").GetString());
            Assert.True(result.GetProperty("totalTagCount").GetInt32() >= 3);
            Assert.Equal(1, result.GetProperty("scenarioFileCount").GetInt32());
            Assert.Equal(1, result.GetProperty("unitFileCount").GetInt32());
            Assert.Equal("active", Assert.Single(result.GetProperty("scenarioFiles").EnumerateArray()).GetProperty("primaryIds")[0].GetString());
            Assert.Equal("Alpha Unit", Assert.Single(result.GetProperty("unitFiles").EnumerateArray()).GetProperty("primaryIds")[0].GetString());
            JsonElement scenarioSummary = Assert.Single(result.GetProperty("scenarios").EnumerateArray());
            JsonElement unitSummary = Assert.Single(result.GetProperty("units").EnumerateArray());
            Assert.Equal("active", scenarioSummary.GetProperty("id").GetString());
            Assert.Equal("Alpha Unit", unitSummary.GetProperty("id").GetString());
            Assert.Equal("Exact", scenarioSummary.GetProperty("provenance").GetProperty("source").GetProperty("precision").GetString());
            Assert.Equal("Exact", unitSummary.GetProperty("provenance").GetProperty("source").GetProperty("precision").GetString());
            Assert.Equal("data/campaigns/Alpha/scenarios/01_Alpha.cfg", scenarioSummary.GetProperty("provenance").GetProperty("source").GetProperty("source").GetString());
            Assert.Equal("data/campaigns/Alpha/units/Alpha_Unit.cfg", unitSummary.GetProperty("provenance").GetProperty("source").GetProperty("source").GetString());
            Assert.Contains(result.GetProperty("sourceFiles").EnumerateArray().Select(item => item.GetString()), path => path == "data/core/_main.cfg");
            Assert.Contains(result.GetProperty("sourceFiles").EnumerateArray().Select(item => item.GetString()), path => path == "data/core/macros/_main.cfg");
            Assert.Equal(result.GetProperty("sourceFiles").GetArrayLength(), result.GetProperty("sourceFileCount").GetInt32());
            Assert.All(result.GetProperty("sourceFiles").EnumerateArray(), item => Assert.False(Path.IsPathRooted(item.GetString())));
        }

        [Fact]
        public async Task All_continues_after_failure_and_orders_campaigns()
        {
            using var installation = SyntheticInstallation.Create();
            installation.WriteCore(string.Empty);
            installation.WriteCampaign("Zulu", Campaign("Zulu", "ZULU", "[broken]\n[/different]\n"));
            installation.WriteCampaign("Alpha", Campaign("Alpha", "ALPHA", "[ok]\n[/ok]\n"));
            string reportPath = Path.Combine(installation.Root, "report.json");

            int exitCode = await Run(installation.Root, reportPath, "--all");

            Assert.Equal(1, exitCode);
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(new[] { "Alpha", "Zulu" }, report.RootElement.GetProperty("campaigns").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
            Assert.Equal(1, report.RootElement.GetProperty("summary").GetProperty("passed").GetInt32());
            Assert.Equal(1, report.RootElement.GetProperty("summary").GetProperty("failed").GetInt32());
            JsonElement failure = report.RootElement.GetProperty("campaigns")[1];
            Assert.Equal("Diagnostics", failure.GetProperty("failureKind").GetString());
            JsonElement parserDiagnostic = Assert.Single(failure.GetProperty("diagnostics").EnumerateArray(), item => item.GetProperty("phase").GetString() == "Parser");
            string? source = parserDiagnostic.GetProperty("span").GetProperty("source").GetString();
            Assert.Equal("data/campaign-validator.cfg", source);
            Assert.False(Path.IsPathRooted(source));
        }

        [Fact]
        public async Task Ambiguous_campaign_metadata_is_a_validation_failure_with_a_report()
        {
            using var installation = SyntheticInstallation.Create();
            installation.WriteCore(string.Empty);
            installation.WriteCampaign("Ambiguous", "[campaign]\nid=One\ndefine=ONE\n[/campaign]\n[campaign]\nid=Two\ndefine=TWO\n[/campaign]\n");
            string reportPath = Path.Combine(installation.Root, "report.json");

            int exitCode = await Run(installation.Root, reportPath, "--campaign", "Ambiguous");

            Assert.Equal(1, exitCode);
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            JsonElement result = Assert.Single(report.RootElement.GetProperty("campaigns").EnumerateArray());
            Assert.Equal("CampaignMetadata", result.GetProperty("failureKind").GetString());
            Assert.Equal("VAL1001", Assert.Single(result.GetProperty("diagnostics").EnumerateArray()).GetProperty("code").GetString());
        }

        [Fact]
        public async Task Scenario_and_unit_file_diagnostics_are_reported_as_child_failures()
        {
            using var installation = SyntheticInstallation.Create();
            installation.WriteCore(string.Empty);
            installation.WriteCampaign("ChildFailure", Campaign("ChildFailure", "CHILD_FAILURE"));
            installation.WriteFile(Path.Combine("data", "campaigns", "ChildFailure", "scenarios", "broken.cfg"), "[scenario]\nid=broken\n[/different]\n");
            installation.WriteFile(Path.Combine("data", "campaigns", "ChildFailure", "units", "ok.cfg"), "[unit_type]\nid=Okay\n[/unit_type]\n");
            string reportPath = Path.Combine(installation.Root, "report.json");

            int exitCode = await Run(installation.Root, reportPath, "--campaign", "ChildFailure");

            Assert.Equal(1, exitCode);
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(1, report.RootElement.GetProperty("summary").GetProperty("scenarioFilesFailed").GetInt32());
            Assert.Equal(1, report.RootElement.GetProperty("summary").GetProperty("unitFilesPassed").GetInt32());
            JsonElement result = Assert.Single(report.RootElement.GetProperty("campaigns").EnumerateArray());
            Assert.Equal("ChildDiagnostics", result.GetProperty("failureKind").GetString());
            JsonElement scenario = Assert.Single(result.GetProperty("scenarioFiles").EnumerateArray());
            Assert.Equal("Failed", scenario.GetProperty("status").GetString());
            Assert.Contains(scenario.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("phase").GetString() == "Parser");
        }

        [Fact]
        public async Task Output_limit_returns_three_and_still_writes_report()
        {
            using var installation = SyntheticInstallation.Create();
            installation.WriteCore("[payload]\nvalue=<<" + new string('x', 1_100_000) + ">>\n[/payload]\n");
            installation.WriteCampaign("Large", Campaign("Large", "LARGE"));
            string reportPath = Path.Combine(installation.Root, "report.json");

            int exitCode = await Run(installation.Root, reportPath, "--campaign", "Large", "--max-output-mib", "1");

            Assert.Equal(3, exitCode);
            using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(1, report.RootElement.GetProperty("summary").GetProperty("resourceLimited").GetInt32());
            JsonElement result = Assert.Single(report.RootElement.GetProperty("campaigns").EnumerateArray());
            Assert.Equal("ResourceLimit", result.GetProperty("status").GetString());
            Assert.Equal("OutputLimit", result.GetProperty("failureKind").GetString());
        }

        [Fact]
        public async Task Missing_or_conflicting_selection_and_unknown_campaign_are_usage_errors()
        {
            using var installation = SyntheticInstallation.Create();
            installation.WriteCore(string.Empty);
            installation.WriteCampaign("Known", Campaign("Known", "KNOWN"));
            string reportPath = Path.Combine(installation.Root, "report.json");

            Assert.Equal(2, await Run(installation.Root, reportPath));
            Assert.Equal(2, await Run(installation.Root, reportPath, "--all", "--campaign", "Known"));
            Assert.Equal(2, await Run(installation.Root, reportPath, "--campaign", "Missing"));
            Assert.False(File.Exists(reportPath));
        }

        [Fact]
        public void Discovery_requires_main_file_and_is_deterministic()
        {
            using var installation = SyntheticInstallation.Create();
            installation.WriteCampaign("zeta", Campaign("zeta", "ZETA"));
            installation.WriteCampaign("Alpha", Campaign("Alpha", "ALPHA"));
            Directory.CreateDirectory(Path.Combine(installation.Root, "data", "campaigns", "Ignored"));

            Assert.Equal(new[] { "Alpha", "zeta" }, CampaignValidationRunner.DiscoverCampaigns(installation.Root));
        }

        [Fact]
        public void PowerShell_wrapper_requires_explicit_selection_without_game_files()
        {
            string script = FindRepositoryFile(Path.Combine("scripts", "Validate-WesnothCampaigns.ps1"));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "-NoProfile", "-File", script, "-InstallationRoot", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) }
            })!;
            process.WaitForExit(30_000);
            Assert.True(process.HasExited);
            Assert.Equal(2, process.ExitCode);
            Assert.Contains("Specify -All or", process.StandardError.ReadToEnd());
        }

        private static async Task<int> Run(string root, string output, params string[] selection)
        {
            string[] arguments = new[] { "--installation-root", root, "--output", output }.Concat(selection).ToArray();
            return await CampaignValidatorApplication.RunAsync(arguments, TextWriter.Null, TextWriter.Null);
        }

        private static string Campaign(string id, string define, string content = "") => $"[campaign]\nid={id}\ndefine={define}\n[/campaign]\n{content}";

        private static string FindRepositoryFile(string relativePath)
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            throw new FileNotFoundException(relativePath);
        }

        private sealed class SyntheticInstallation : IDisposable
        {
            public string Root { get; }

            private SyntheticInstallation(string root) { Root = root; }

            public static SyntheticInstallation Create()
            {
                string root = Path.Combine(Path.GetTempPath(), "WmlCampaignValidatorTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(root, "data", "core"));
                Directory.CreateDirectory(Path.Combine(root, "data", "campaigns"));
                return new SyntheticInstallation(root);
            }

            public void WriteCore(string source) => File.WriteAllText(Path.Combine(Root, "data", "core", "_main.cfg"), source);

            public void WriteFile(string relativePath, string source)
            {
                string path = Path.Combine(Root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, source);
            }

            public void WriteCampaign(string name, string source)
            {
                string directory = Path.Combine(Root, "data", "campaigns", name);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "_main.cfg"), source);
            }

            public void Dispose()
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
            }
        }
    }
}
