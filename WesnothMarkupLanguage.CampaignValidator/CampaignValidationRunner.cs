using System.Diagnostics;
using System.Reflection;
using System.Text;
using WesnothMarkupLanguage;

namespace WesnothMarkupLanguage.CampaignValidator;

public static class CampaignValidationRunner
{
    public static IReadOnlyList<string> DiscoverCampaigns(string installationRoot)
    {
        string campaignsRoot = Path.Combine(Path.GetFullPath(installationRoot), "data", "campaigns");
        if (!Directory.Exists(campaignsRoot)) return Array.Empty<string>();
        return Directory.GetDirectories(campaignsRoot).Where(path => File.Exists(Path.Combine(path, "_main.cfg"))).Select(Path.GetFileName).Where(name => !string.IsNullOrEmpty(name)).Cast<string>().OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    public static async Task<CampaignValidationReport> ValidateAsync(CampaignValidatorOptions options, CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(options.InstallationRoot), dataRoot = Path.Combine(root, "data"); var available = DiscoverCampaigns(root);
        var selected = options.AllCampaigns ? available : options.Campaigns.Select(name => available.First(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var report = new CampaignValidationReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            ToolVersion = VersionOf(typeof(CampaignValidationRunner).Assembly),
            LibraryVersion = VersionOf(typeof(WmlParser).Assembly),
            InstallationRoot = root,
            Configuration = new ValidationConfiguration { Selection = options.AllCampaigns ? "All" : "Selected", Difficulty = "NORMAL", WesnothVersion = "1.18.7", MaxOutputBytes = checked((long)options.MaxOutputMiB * 1024 * 1024) }
        };
        foreach (string campaign in selected) { cancellationToken.ThrowIfCancellationRequested(); report.Campaigns.Add(await ValidateCampaignAsync(root, dataRoot, campaign, report.Configuration.MaxOutputBytes, cancellationToken)); }
        report.Summary = new ValidationSummary
        {
            Total = report.Campaigns.Count,
            Passed = report.Campaigns.Count(item => item.Status == "Passed"),
            Failed = report.Campaigns.Count(item => item.Status == "Failed"),
            ResourceLimited = report.Campaigns.Count(item => item.Status == "ResourceLimit"),
            Warnings = report.Campaigns.Sum(item => item.Diagnostics.Count(diagnostic => diagnostic.Severity == "Warning")),
            Diagnostics = report.Campaigns.Sum(item => item.Diagnostics.Count)
        };
        return report;
    }

    private static async Task<CampaignValidationResult> ValidateCampaignAsync(string root, string dataRoot, string campaign, long maxOutputBytes, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew(); string entry = Path.Combine(dataRoot, "campaigns", campaign, "_main.cfg");
        var result = new CampaignValidationResult { Name = campaign, EntryPath = Logical(entry, root) };
        try
        {
            string source = await File.ReadAllTextAsync(entry, cancellationToken); var metadataSyntax = WmlParser.Parse(source, entry); var candidates = metadataSyntax.Document.FindTags("campaign").ToArray();
            var matching = candidates.Where(tag => string.Equals(tag.GetAttribute("id"), campaign, StringComparison.OrdinalIgnoreCase)).ToArray(); WmlTag? metadata = matching.Length == 1 ? matching[0] : candidates.Length == 1 ? candidates[0] : null;
            if (metadata == null) return Fail(result, "CampaignMetadata", "VAL1001", $"Campaign metadata for '{campaign}' is missing or ambiguous.", stopwatch);
            result.CampaignId = metadata.GetAttribute("id"); result.CampaignDefine = metadata.GetAttribute("define");
            if (string.IsNullOrWhiteSpace(result.CampaignDefine)) return Fail(result, "CampaignMetadata", "VAL1002", $"Campaign '{campaign}' does not declare a define attribute.", stopwatch);
            var resolver = new FileSystemWmlSourceResolver(dataRoot); var preprocessorOptions = new WmlPreprocessorOptions { SourceResolver = resolver, MaxOutputBytes = maxOutputBytes };
            preprocessorOptions.Defines[result.CampaignDefine] = string.Empty; preprocessorOptions.Defines["NORMAL"] = string.Empty;
            string composite = "{core/}\n{campaigns/" + campaign + "/}\n", syntheticSource = Path.Combine(dataRoot, "campaign-validator.cfg");
            var processed = await WmlPreprocessor.ProcessAsync(composite, preprocessorOptions, syntheticSource, cancellationToken);
            result.ExpandedCharacters = processed.Text.Length; result.ExpandedUtf8Bytes = Encoding.UTF8.GetByteCount(processed.Text); result.MacroCount = processed.Macros.Count; result.SourceMapEntryCount = processed.SourceMap.Count;
            result.SourceFiles = processed.SourceMap.Select(entry => NormalizeSource(entry.Source, root)).Where(path => path != null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal).ToList(); result.SourceFileCount = result.SourceFiles.Count;
            result.RootTagCount = processed.Syntax.Document.Tags.Count(); result.TotalTagCount = CountTags(processed.Syntax.Document.Children); result.AttributeCount = CountAttributes(processed.Syntax.Document.Children);
            result.Diagnostics.AddRange(processed.Diagnostics.Select(diagnostic => ConvertDiagnostic("Preprocessor", diagnostic, root))); result.Diagnostics.AddRange(processed.Syntax.Diagnostics.Select(diagnostic => ConvertDiagnostic("Parser", diagnostic, root)));
            result.Status = result.Diagnostics.Any(diagnostic => diagnostic.Severity == "Error") ? "Failed" : "Passed"; result.FailureKind = result.Status == "Failed" ? "Diagnostics" : null;
        }
        catch (WmlException ex) when (ex.Message.IndexOf("Maximum preprocessor output size exceeded", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            result.Status = "ResourceLimit"; result.FailureKind = "OutputLimit"; result.Diagnostics.Add(ToolDiagnostic("VAL2001", ex.Message));
        }
        catch (OperationCanceledException) { throw; }
        stopwatch.Stop(); result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds; return result;
    }

    private static CampaignValidationResult Fail(CampaignValidationResult result, string kind, string code, string message, Stopwatch stopwatch) { result.Status = "Failed"; result.FailureKind = kind; result.Diagnostics.Add(ToolDiagnostic(code, message)); stopwatch.Stop(); result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds; return result; }
    private static ValidationDiagnostic ToolDiagnostic(string code, string message) => new() { Phase = "Tool", Code = code, Severity = "Error", Message = message };
    private static ValidationDiagnostic ConvertDiagnostic(string phase, WmlDiagnostic diagnostic, string root) => new()
    {
        Phase = phase, Code = diagnostic.Code, Severity = diagnostic.Severity.ToString(), Message = diagnostic.Message,
        Span = new ValidationSpan { Source = NormalizeSource(diagnostic.Span.Source, root), Start = diagnostic.Span.Start, Length = diagnostic.Span.Length, Line = diagnostic.Span.Line, Column = diagnostic.Span.Column },
        PreprocessorContext = diagnostic.PreprocessorContext == null ? null : new ValidationPreprocessorContext { Expression = diagnostic.PreprocessorContext.Expression, Symbol = diagnostic.PreprocessorContext.Symbol, ExpressionKind = diagnostic.PreprocessorContext.ExpressionKind.ToString(), MacroWasRegistered = diagnostic.PreprocessorContext.MacroWasRegistered, IncludeFallbackAttempted = diagnostic.PreprocessorContext.IncludeFallbackAttempted, IncludeCandidate = diagnostic.PreprocessorContext.IncludeCandidate }
    };
    private static string? NormalizeSource(string? source, string root) { if (source == null) return null; if (!Path.IsPathRooted(source)) return source.Replace('\\', '/'); string full = Path.GetFullPath(source), prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar; return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(root, full).Replace('\\', '/') : full.Replace('\\', '/'); }
    private static string Logical(string path, string root) => NormalizeSource(path, root)!;
    private static int CountTags(IEnumerable<WmlNode> nodes) => nodes.Sum(node => node is WmlTag tag ? 1 + CountTags(tag.Children) : 0);
    private static int CountAttributes(IEnumerable<WmlNode> nodes) => nodes.Sum(node => node is WmlAttribute ? 1 : node is WmlTag tag ? CountAttributes(tag.Children) : 0);
    private static string VersionOf(Assembly assembly) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
}

public sealed class CampaignValidationReport { public string SchemaVersion { get; set; } = "1.0"; public string ToolVersion { get; set; } = ""; public string LibraryVersion { get; set; } = ""; public DateTimeOffset GeneratedAtUtc { get; set; } public string InstallationRoot { get; set; } = ""; public ValidationConfiguration Configuration { get; set; } = new(); public ValidationSummary Summary { get; set; } = new(); public List<CampaignValidationResult> Campaigns { get; set; } = new(); }
public sealed class ValidationConfiguration { public string Selection { get; set; } = ""; public string Difficulty { get; set; } = "NORMAL"; public string WesnothVersion { get; set; } = "1.18.7"; public long MaxOutputBytes { get; set; } }
public sealed class ValidationSummary { public int Total { get; set; } public int Passed { get; set; } public int Failed { get; set; } public int ResourceLimited { get; set; } public int Warnings { get; set; } public int Diagnostics { get; set; } }
public sealed class CampaignValidationResult { public string Name { get; set; } = ""; public string? CampaignId { get; set; } public string? CampaignDefine { get; set; } public string EntryPath { get; set; } = ""; public string Status { get; set; } = "Failed"; public string? FailureKind { get; set; } public long ElapsedMilliseconds { get; set; } public int ExpandedCharacters { get; set; } public int ExpandedUtf8Bytes { get; set; } public int MacroCount { get; set; } public int SourceMapEntryCount { get; set; } public int SourceFileCount { get; set; } public int RootTagCount { get; set; } public int TotalTagCount { get; set; } public int AttributeCount { get; set; } public List<string> SourceFiles { get; set; } = new(); public List<ValidationDiagnostic> Diagnostics { get; set; } = new(); }
public sealed class ValidationDiagnostic { public string Phase { get; set; } = ""; public string Code { get; set; } = ""; public string Severity { get; set; } = ""; public string Message { get; set; } = ""; public ValidationSpan? Span { get; set; } public ValidationPreprocessorContext? PreprocessorContext { get; set; } }
public sealed class ValidationSpan { public string? Source { get; set; } public int Start { get; set; } public int Length { get; set; } public int Line { get; set; } public int Column { get; set; } }
public sealed class ValidationPreprocessorContext { public string Expression { get; set; } = ""; public string? Symbol { get; set; } public string ExpressionKind { get; set; } = ""; public bool MacroWasRegistered { get; set; } public bool IncludeFallbackAttempted { get; set; } public string? IncludeCandidate { get; set; } }
