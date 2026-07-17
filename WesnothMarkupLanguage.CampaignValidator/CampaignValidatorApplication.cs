using System.Text.Json;
using System.Text.Json.Serialization;

namespace WesnothMarkupLanguage.CampaignValidator;

public static class CampaignValidatorApplication
{
    public const int SuccessExitCode = 0, ValidationFailureExitCode = 1, UsageExitCode = 2, ResourceLimitExitCode = 3, ToolFailureExitCode = 4, CancelledExitCode = 130;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = ParseArguments(args);
            if (options.ShowHelp) { await output.WriteLineAsync(Usage()); return SuccessExitCode; }
            ValidateOptions(options);
            var report = await CampaignValidationRunner.ValidateAsync(options, cancellationToken);
            await WriteReportAsync(report, options.OutputPath, cancellationToken);
            await output.WriteLineAsync($"Campaign validation report: {options.OutputPath}");
            if (report.Summary.ResourceLimited > 0) return ResourceLimitExitCode;
            return report.Summary.Failed > 0 ? ValidationFailureExitCode : SuccessExitCode;
        }
        catch (OperationCanceledException) { await error.WriteLineAsync("Campaign validation was cancelled."); return CancelledExitCode; }
        catch (CampaignValidatorUsageException ex) { await error.WriteLineAsync(ex.Message); await error.WriteLineAsync(Usage()); return UsageExitCode; }
        catch (Exception ex) { await error.WriteLineAsync($"Campaign validator failed: {ex.Message}"); return ToolFailureExitCode; }
    }

    public static CampaignValidatorOptions ParseArguments(IReadOnlyList<string> args)
    {
        var options = new CampaignValidatorOptions();
        for (int i = 0; i < args.Count; i++)
        {
            string argument = args[i];
            switch (argument)
            {
                case "--installation-root": options.InstallationRoot = Value(args, ref i, argument); break;
                case "--campaign": options.Campaigns.Add(Value(args, ref i, argument)); break;
                case "--all": options.AllCampaigns = true; break;
                case "--output": options.OutputPath = Value(args, ref i, argument); break;
                case "--max-output-mib":
                    if (!int.TryParse(Value(args, ref i, argument), out int limit) || limit <= 0) throw new CampaignValidatorUsageException("--max-output-mib must be a positive integer.");
                    options.MaxOutputMiB = limit; break;
                case "--help": case "-h": options.ShowHelp = true; break;
                default: throw new CampaignValidatorUsageException($"Unknown argument '{argument}'.");
            }
        }
        options.InstallationRoot = Path.GetFullPath(options.InstallationRoot);
        options.OutputPath = Path.GetFullPath(options.OutputPath);
        return options;
    }

    private static void ValidateOptions(CampaignValidatorOptions options)
    {
        if (options.AllCampaigns && options.Campaigns.Count > 0) throw new CampaignValidatorUsageException("Specify either --all or at least one --campaign, but not both.");
        if (!options.AllCampaigns && options.Campaigns.Count == 0)
        {
            var discovered = CampaignValidationRunner.DiscoverCampaigns(options.InstallationRoot);
            string availability = discovered.Count == 0 ? "No campaigns were discovered." : $"Available campaigns: {string.Join(", ", discovered)}";
            throw new CampaignValidatorUsageException($"Specify --all or at least one --campaign. {availability}");
        }
        string data = Path.Combine(options.InstallationRoot, "data"), campaigns = Path.Combine(data, "campaigns"), core = Path.Combine(data, "core", "_main.cfg");
        if (!Directory.Exists(campaigns) || !File.Exists(core)) throw new CampaignValidatorUsageException($"Installation root '{options.InstallationRoot}' must contain data/core/_main.cfg and data/campaigns.");
        var available = CampaignValidationRunner.DiscoverCampaigns(options.InstallationRoot);
        var unknown = options.Campaigns.Where(requested => !available.Contains(requested, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (unknown.Length > 0) throw new CampaignValidatorUsageException($"Unknown campaign(s): {string.Join(", ", unknown)}. Available campaigns: {string.Join(", ", available)}");
    }

    private static async Task WriteReportAsync(CampaignValidationReport report, string outputPath, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(outputPath); if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true)) await JsonSerializer.SerializeAsync(stream, report, options, cancellationToken);
            File.Move(temporary, outputPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string Value(IReadOnlyList<string> args, ref int index, string option) { if (++index >= args.Count) throw new CampaignValidatorUsageException($"{option} requires a value."); return args[index]; }
    private static string Usage() => "Usage: dotnet run --project WesnothMarkupLanguage.CampaignValidator -- (--all | --campaign NAME [--campaign NAME ...]) [--installation-root PATH] [--output PATH] [--max-output-mib NUMBER]";
}

public sealed class CampaignValidatorOptions
{
    public string InstallationRoot { get; set; } = Path.Combine("References", "Wesnoth-Installation");
    public string OutputPath { get; set; } = Path.Combine("artifacts", "validation", "campaign-validation.json");
    public List<string> Campaigns { get; } = new();
    public bool AllCampaigns { get; set; }
    public int MaxOutputMiB { get; set; } = 128;
    public bool ShowHelp { get; set; }
}

public sealed class CampaignValidatorUsageException : Exception { public CampaignValidatorUsageException(string message) : base(message) { } }
