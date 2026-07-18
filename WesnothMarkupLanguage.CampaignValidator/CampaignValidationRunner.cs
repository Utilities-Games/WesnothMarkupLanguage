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
        return Directory.GetDirectories(campaignsRoot)
            .Where(path => File.Exists(Path.Combine(path, "_main.cfg")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task<CampaignValidationReport> ValidateAsync(CampaignValidatorOptions options, CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(options.InstallationRoot);
        string dataRoot = Path.Combine(root, "data");
        var available = DiscoverCampaigns(root);
        var selected = options.AllCampaigns
            ? available
            : options.Campaigns
                .Select(name => available.First(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

        var report = new CampaignValidationReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            ToolVersion = VersionOf(typeof(CampaignValidationRunner).Assembly),
            LibraryVersion = VersionOf(typeof(WmlParser).Assembly),
            InstallationRoot = root,
            Configuration = new ValidationConfiguration
            {
                Selection = options.AllCampaigns ? "All" : "Selected",
                Difficulty = "NORMAL",
                WesnothVersion = "1.18.7",
                MaxOutputBytes = checked((long)options.MaxOutputMiB * 1024 * 1024)
            }
        };

        foreach (string campaign in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            report.Campaigns.Add(await ValidateCampaignAsync(root, dataRoot, campaign, report.Configuration.MaxOutputBytes, cancellationToken));
        }

        report.Summary = BuildSummary(report.Campaigns);
        return report;
    }

    private static async Task<CampaignValidationResult> ValidateCampaignAsync(string root, string dataRoot, string campaign, long maxOutputBytes, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string campaignRoot = Path.Combine(dataRoot, "campaigns", campaign);
        string entry = Path.Combine(campaignRoot, "_main.cfg");
        var result = new CampaignValidationResult { Name = campaign, EntryPath = Logical(entry, root) };

        result.ScenarioFiles = await ValidateFileSetAsync(root, Path.Combine(campaignRoot, "scenarios"), "ScenarioFile", "scenario", cancellationToken);
        result.UnitFiles = await ValidateFileSetAsync(root, Path.Combine(campaignRoot, "units"), "UnitFile", "unit_type", cancellationToken);
        result.ScenarioFileCount = result.ScenarioFiles.Count;
        result.UnitFileCount = result.UnitFiles.Count;

        try
        {
            string source = await File.ReadAllTextAsync(entry, cancellationToken);
            var metadataSyntax = WmlParser.Parse(source, entry);
            var candidates = metadataSyntax.Document.FindTags("campaign").ToArray();
            var matching = candidates.Where(tag => string.Equals(tag.GetAttribute("id"), campaign, StringComparison.OrdinalIgnoreCase)).ToArray();
            WmlTag? metadata = matching.Length == 1 ? matching[0] : candidates.Length == 1 ? candidates[0] : null;
            if (metadata == null)
            {
                AddFailure(result, "CampaignMetadata", "VAL1001", $"Campaign metadata for '{campaign}' is missing or ambiguous.");
                return Complete(result, stopwatch);
            }

            result.CampaignId = metadata.GetAttribute("id");
            result.CampaignDefine = metadata.GetAttribute("define");
            if (string.IsNullOrWhiteSpace(result.CampaignDefine))
            {
                AddFailure(result, "CampaignMetadata", "VAL1002", $"Campaign '{campaign}' does not declare a define attribute.");
                return Complete(result, stopwatch);
            }

            var resolver = new FileSystemWmlSourceResolver(dataRoot);
            var preprocessorOptions = new WmlPreprocessorOptions { SourceResolver = resolver, MaxOutputBytes = maxOutputBytes };
            preprocessorOptions.Defines[result.CampaignDefine] = string.Empty;
            preprocessorOptions.Defines["NORMAL"] = string.Empty;

            string composite = "{core/}\n{campaigns/" + campaign + "/}\n";
            string syntheticSource = Path.Combine(dataRoot, "campaign-validator.cfg");
            var processed = await WmlPreprocessor.ProcessAsync(composite, preprocessorOptions, syntheticSource, cancellationToken);

            result.ExpandedCharacters = processed.Text.Length;
            result.ExpandedUtf8Bytes = Encoding.UTF8.GetByteCount(processed.Text);
            result.MacroCount = processed.Macros.Count;
            result.SourceMapEntryCount = processed.SourceMap.Count;
            result.SourceFiles = processed.SourceMap
                .Select(entry => NormalizeSource(entry.Source, root))
                .Where(path => path != null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            result.SourceFileCount = result.SourceFiles.Count;
            result.RootTagCount = processed.Syntax.Document.Tags.Count();
            result.TotalTagCount = CountTags(processed.Syntax.Document.Children);
            result.AttributeCount = CountAttributes(processed.Syntax.Document.Children);
            result.Scenarios = ExtractScenarios(processed, root);
            result.Units = ExtractUnits(processed, root, campaign);
            result.ScenarioCount = result.Scenarios.Count;
            result.UnitTypeCount = result.Units.Count;
            result.Diagnostics.AddRange(processed.Diagnostics.Select(diagnostic => ConvertDiagnostic("Preprocessor", diagnostic, root)));
            result.Diagnostics.AddRange(processed.Syntax.Diagnostics.Select(diagnostic => ConvertDiagnostic("Parser", diagnostic, root)));
        }
        catch (WmlException ex) when (ex.Message.IndexOf("Maximum preprocessor output size exceeded", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            result.Status = "ResourceLimit";
            result.FailureKind = "OutputLimit";
            result.Diagnostics.Add(ToolDiagnostic("VAL2001", ex.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        return Complete(result, stopwatch);
    }

    private static CampaignValidationResult Complete(CampaignValidationResult result, Stopwatch stopwatch)
    {
        if (result.Status != "ResourceLimit")
        {
            bool hasErrors = HasError(result.Diagnostics);
            bool hasChildFailures = result.ScenarioFiles.Any(file => file.Status != "Passed") || result.UnitFiles.Any(file => file.Status != "Passed");
            result.Status = hasErrors || hasChildFailures ? "Failed" : "Passed";
            result.FailureKind = result.Status == "Passed" ? null : result.FailureKind ?? (hasErrors ? "Diagnostics" : "ChildDiagnostics");
        }

        stopwatch.Stop();
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        return result;
    }

    private static async Task<List<ValidationFileResult>> ValidateFileSetAsync(string root, string directory, string phase, string primaryTag, CancellationToken cancellationToken)
    {
        var results = new List<ValidationFileResult>();
        if (!Directory.Exists(directory)) return results;

        foreach (string file in Directory.GetFiles(directory, "*.cfg", SearchOption.AllDirectories).OrderBy(path => Logical(path, root), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var result = new ValidationFileResult { Path = Logical(file, root), Phase = phase };
            try
            {
                string source = await File.ReadAllTextAsync(file, cancellationToken);
                var syntax = WmlParser.Parse(source, file);
                result.RootTagCount = syntax.Document.Tags.Count();
                result.TotalTagCount = CountTags(syntax.Document.Children);
                result.AttributeCount = CountAttributes(syntax.Document.Children);
                result.PrimaryIds = syntax.Document.FindTags(primaryTag)
                    .Select(tag => tag.GetAttribute("id"))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();
                result.TopLevelTags = syntax.Document.Tags
                    .Select(tag => tag.Name)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
                result.Diagnostics.AddRange(syntax.Diagnostics.Select(diagnostic => ConvertDiagnostic("Parser", diagnostic, root)));
            }
            catch (IOException ex)
            {
                result.Diagnostics.Add(ToolDiagnostic("VAL3001", $"Could not read '{result.Path}': {ex.Message}"));
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Diagnostics.Add(ToolDiagnostic("VAL3001", $"Could not read '{result.Path}': {ex.Message}"));
            }

            result.Status = HasError(result.Diagnostics) ? "Failed" : "Passed";
            result.FailureKind = result.Status == "Failed" ? "Diagnostics" : null;
            stopwatch.Stop();
            result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            results.Add(result);
        }

        return results;
    }

    private static List<CampaignScenarioInfo> ExtractScenarios(WmlPreprocessorResult processed, string root)
        => processed.Syntax.Document.FindTags("scenario")
            .Select(tag => new CampaignScenarioInfo
            {
                Id = tag.GetAttribute("id"),
                Name = tag.GetAttribute("name"),
                NextScenario = tag.GetAttribute("next_scenario"),
                MapData = tag.GetAttribute("map_data"),
                MapFile = tag.GetAttribute("map_file"),
                Source = MapSource(processed.SourceMap, tag.Span?.Start, root),
                Provenance = ConvertProvenance(tag.Provenance, root),
                SideCount = tag.FindTags("side", true).Count(),
                EventCount = tag.FindTags("event", true).Count(),
                ObjectiveCount = tag.FindTags("objective", true).Count(),
                TotalTagCount = 1 + CountTags(tag.Children),
                AttributeCount = CountAttributes(tag.Children)
            })
            .OrderBy(item => item.Id ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(item => item.Name ?? string.Empty, StringComparer.Ordinal)
            .ToList();

    private static List<CampaignUnitInfo> ExtractUnits(WmlPreprocessorResult processed, string root, string campaign)
        => processed.Syntax.Document.FindTags("unit_type")
            .Select(tag =>
            {
                string? source = MapSource(processed.SourceMap, tag.Span?.Start, root);
                return new CampaignUnitInfo
            {
                Id = tag.GetAttribute("id"),
                Name = tag.GetAttribute("name"),
                Race = tag.GetAttribute("race"),
                Level = tag.GetAttribute("level"),
                AdvancesTo = tag.GetAttribute("advances_to"),
                MovementType = tag.GetAttribute("movement_type"),
                Hitpoints = tag.GetAttribute("hitpoints"),
                Movement = tag.GetAttribute("movement"),
                Source = source,
                Provenance = ConvertProvenance(tag.Provenance, root),
                AttackCount = tag.FindTags("attack", true).Count(),
                AbilityTagCount = tag.FindTags("abilities", true).Sum(abilities => abilities.Tags.Count()),
                VariationCount = tag.FindTags("variation", true).Count(),
                TotalTagCount = 1 + CountTags(tag.Children),
                AttributeCount = CountAttributes(tag.Children)
            };
            })
            .Where(item => item.Source != null && item.Source.StartsWith("data/campaigns/" + campaign + "/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Id ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(item => item.Name ?? string.Empty, StringComparer.Ordinal)
            .ToList();

    private static void AddFailure(CampaignValidationResult result, string kind, string code, string message)
    {
        result.Status = "Failed";
        result.FailureKind = kind;
        result.Diagnostics.Add(ToolDiagnostic(code, message));
    }

    private static ValidationSummary BuildSummary(IEnumerable<CampaignValidationResult> campaigns)
    {
        var list = campaigns.ToList();
        return new ValidationSummary
        {
            Total = list.Count,
            Passed = list.Count(item => item.Status == "Passed"),
            Failed = list.Count(item => item.Status == "Failed"),
            ResourceLimited = list.Count(item => item.Status == "ResourceLimit"),
            Warnings = list.Sum(CountWarnings),
            Diagnostics = list.Sum(CountDiagnostics),
            ScenarioFiles = list.Sum(item => item.ScenarioFileCount),
            ScenarioFilesPassed = list.Sum(item => item.ScenarioFiles.Count(file => file.Status == "Passed")),
            ScenarioFilesFailed = list.Sum(item => item.ScenarioFiles.Count(file => file.Status == "Failed")),
            Scenarios = list.Sum(item => item.ScenarioCount),
            UnitFiles = list.Sum(item => item.UnitFileCount),
            UnitFilesPassed = list.Sum(item => item.UnitFiles.Count(file => file.Status == "Passed")),
            UnitFilesFailed = list.Sum(item => item.UnitFiles.Count(file => file.Status == "Failed")),
            UnitTypes = list.Sum(item => item.UnitTypeCount)
        };
    }

    private static int CountWarnings(CampaignValidationResult result)
        => result.Diagnostics.Count(diagnostic => diagnostic.Severity == "Warning")
           + result.ScenarioFiles.Sum(file => file.Diagnostics.Count(diagnostic => diagnostic.Severity == "Warning"))
           + result.UnitFiles.Sum(file => file.Diagnostics.Count(diagnostic => diagnostic.Severity == "Warning"));

    private static int CountDiagnostics(CampaignValidationResult result)
        => result.Diagnostics.Count
           + result.ScenarioFiles.Sum(file => file.Diagnostics.Count)
           + result.UnitFiles.Sum(file => file.Diagnostics.Count);

    private static bool HasError(IEnumerable<ValidationDiagnostic> diagnostics) => diagnostics.Any(diagnostic => diagnostic.Severity == "Error");

    private static ValidationDiagnostic ToolDiagnostic(string code, string message) => new() { Phase = "Tool", Code = code, Severity = "Error", Message = message };

    private static ValidationDiagnostic ConvertDiagnostic(string phase, WmlDiagnostic diagnostic, string root) => new()
    {
        Phase = phase,
        Code = diagnostic.Code,
        Severity = diagnostic.Severity.ToString(),
        Message = diagnostic.Message,
        Span = new ValidationSpan
        {
            Source = NormalizeSource(diagnostic.Span.Source, root),
            Start = diagnostic.Span.Start,
            Length = diagnostic.Span.Length,
            Line = diagnostic.Span.Line,
            Column = diagnostic.Span.Column
        },
        PreprocessorContext = diagnostic.PreprocessorContext == null
            ? null
            : new ValidationPreprocessorContext
            {
                Expression = diagnostic.PreprocessorContext.Expression,
                Symbol = diagnostic.PreprocessorContext.Symbol,
                ExpressionKind = diagnostic.PreprocessorContext.ExpressionKind.ToString(),
                MacroWasRegistered = diagnostic.PreprocessorContext.MacroWasRegistered,
                IncludeFallbackAttempted = diagnostic.PreprocessorContext.IncludeFallbackAttempted,
                IncludeCandidate = diagnostic.PreprocessorContext.IncludeCandidate
            },
        Provenance = ConvertProvenance(diagnostic.Provenance, root)
    };

    private static ValidationProvenance? ConvertProvenance(WmlExpansionProvenance? provenance, string root)
        => provenance == null
            ? null
            : new ValidationProvenance
            {
                Source = ConvertSourceReference(provenance.Source, root),
                ExpansionChain = provenance.ExpansionChain.Select(frame => new ValidationMacroExpansionFrame
                {
                    MacroSymbol = frame.MacroSymbol,
                    Definition = ConvertSourceReference(frame.Definition, root),
                    Invocation = ConvertSourceReference(frame.Invocation, root)
                }).ToList()
            };

    private static ValidationSourceReference ConvertSourceReference(WmlSourceReference source, string root) => new()
    {
        Source = NormalizeSource(source.Source, root),
        Precision = source.Precision.ToString(),
        Start = source.Start,
        Length = source.Length,
        Line = source.Line,
        Column = source.Column
    };

    private static string? NormalizeSource(string? source, string root)
    {
        if (source == null) return null;
        if (!Path.IsPathRooted(source)) return source.Replace('\\', '/');
        string full = Path.GetFullPath(source);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(root, full).Replace('\\', '/') : full.Replace('\\', '/');
    }

    private static string Logical(string path, string root) => NormalizeSource(path, root)!;

    private static string? MapSource(IReadOnlyList<WmlSourceMapEntry> sourceMap, int? outputStart, string root)
    {
        if (outputStart == null) return null;
        var entry = sourceMap
            .Where(item => item.OutputStart <= outputStart.Value && outputStart.Value <= item.OutputStart + item.OutputLength)
            .OrderBy(item => item.OutputLength)
            .FirstOrDefault();
        return NormalizeSource(entry?.Source, root);
    }

    private static int CountTags(IEnumerable<WmlNode> nodes) => nodes.Sum(node => node is WmlTag tag ? 1 + CountTags(tag.Children) : 0);
    private static int CountAttributes(IEnumerable<WmlNode> nodes) => nodes.Sum(node => node is WmlAttribute ? 1 : node is WmlTag tag ? CountAttributes(tag.Children) : 0);
    private static string VersionOf(Assembly assembly) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
}

public sealed class CampaignValidationReport
{
    public string SchemaVersion { get; set; } = "1.2";
    public string ToolVersion { get; set; } = "";
    public string LibraryVersion { get; set; } = "";
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string InstallationRoot { get; set; } = "";
    public ValidationConfiguration Configuration { get; set; } = new();
    public ValidationSummary Summary { get; set; } = new();
    public List<CampaignValidationResult> Campaigns { get; set; } = new();
}

public sealed class ValidationConfiguration
{
    public string Selection { get; set; } = "";
    public string Difficulty { get; set; } = "NORMAL";
    public string WesnothVersion { get; set; } = "1.18.7";
    public long MaxOutputBytes { get; set; }
}

public sealed class ValidationSummary
{
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int ResourceLimited { get; set; }
    public int Warnings { get; set; }
    public int Diagnostics { get; set; }
    public int ScenarioFiles { get; set; }
    public int ScenarioFilesPassed { get; set; }
    public int ScenarioFilesFailed { get; set; }
    public int Scenarios { get; set; }
    public int UnitFiles { get; set; }
    public int UnitFilesPassed { get; set; }
    public int UnitFilesFailed { get; set; }
    public int UnitTypes { get; set; }
}

public sealed class CampaignValidationResult
{
    public string Name { get; set; } = "";
    public string? CampaignId { get; set; }
    public string? CampaignDefine { get; set; }
    public string EntryPath { get; set; } = "";
    public string Status { get; set; } = "Failed";
    public string? FailureKind { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int ExpandedCharacters { get; set; }
    public int ExpandedUtf8Bytes { get; set; }
    public int MacroCount { get; set; }
    public int SourceMapEntryCount { get; set; }
    public int SourceFileCount { get; set; }
    public int RootTagCount { get; set; }
    public int TotalTagCount { get; set; }
    public int AttributeCount { get; set; }
    public int ScenarioFileCount { get; set; }
    public int ScenarioCount { get; set; }
    public int UnitFileCount { get; set; }
    public int UnitTypeCount { get; set; }
    public List<string> SourceFiles { get; set; } = new();
    public List<ValidationFileResult> ScenarioFiles { get; set; } = new();
    public List<ValidationFileResult> UnitFiles { get; set; } = new();
    public List<CampaignScenarioInfo> Scenarios { get; set; } = new();
    public List<CampaignUnitInfo> Units { get; set; } = new();
    public List<ValidationDiagnostic> Diagnostics { get; set; } = new();
}

public sealed class ValidationFileResult
{
    public string Path { get; set; } = "";
    public string Phase { get; set; } = "";
    public string Status { get; set; } = "Failed";
    public string? FailureKind { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int RootTagCount { get; set; }
    public int TotalTagCount { get; set; }
    public int AttributeCount { get; set; }
    public List<string> PrimaryIds { get; set; } = new();
    public List<string> TopLevelTags { get; set; } = new();
    public List<ValidationDiagnostic> Diagnostics { get; set; } = new();
}

public sealed class CampaignScenarioInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? NextScenario { get; set; }
    public string? MapData { get; set; }
    public string? MapFile { get; set; }
    public string? Source { get; set; }
    public ValidationProvenance? Provenance { get; set; }
    public int SideCount { get; set; }
    public int EventCount { get; set; }
    public int ObjectiveCount { get; set; }
    public int TotalTagCount { get; set; }
    public int AttributeCount { get; set; }
}

public sealed class CampaignUnitInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Race { get; set; }
    public string? Level { get; set; }
    public string? AdvancesTo { get; set; }
    public string? MovementType { get; set; }
    public string? Hitpoints { get; set; }
    public string? Movement { get; set; }
    public string? Source { get; set; }
    public ValidationProvenance? Provenance { get; set; }
    public int AttackCount { get; set; }
    public int AbilityTagCount { get; set; }
    public int VariationCount { get; set; }
    public int TotalTagCount { get; set; }
    public int AttributeCount { get; set; }
}

public sealed class ValidationDiagnostic
{
    public string Phase { get; set; } = "";
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public ValidationSpan? Span { get; set; }
    public ValidationPreprocessorContext? PreprocessorContext { get; set; }
    public ValidationProvenance? Provenance { get; set; }
}

public sealed class ValidationSpan
{
    public string? Source { get; set; }
    public int Start { get; set; }
    public int Length { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}

public sealed class ValidationPreprocessorContext
{
    public string Expression { get; set; } = "";
    public string? Symbol { get; set; }
    public string ExpressionKind { get; set; } = "";
    public bool MacroWasRegistered { get; set; }
    public bool IncludeFallbackAttempted { get; set; }
    public string? IncludeCandidate { get; set; }
}

public sealed class ValidationProvenance
{
    public ValidationSourceReference Source { get; set; } = new();
    public List<ValidationMacroExpansionFrame> ExpansionChain { get; set; } = new();
}

public sealed class ValidationMacroExpansionFrame
{
    public string MacroSymbol { get; set; } = "";
    public ValidationSourceReference Definition { get; set; } = new();
    public ValidationSourceReference Invocation { get; set; } = new();
}

public sealed class ValidationSourceReference
{
    public string? Source { get; set; }
    public string Precision { get; set; } = "";
    public int Start { get; set; }
    public int Length { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}
