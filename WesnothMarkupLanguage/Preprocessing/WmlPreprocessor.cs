using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WesnothMarkupLanguage
{
    public sealed class WmlSource
    {
        public WmlSource(string path, string text) : this(path, text, null) { }
        public WmlSource(string path, string text, IEnumerable<WmlSourceSegment>? segments) { Path = path; Text = text; Segments = segments == null ? Array.Empty<WmlSourceSegment>() : new List<WmlSourceSegment>(segments); }
        public string Path { get; }
        public string Text { get; }
        public IReadOnlyList<WmlSourceSegment> Segments { get; }
    }

    public sealed class WmlSourceSegment
    {
        public WmlSourceSegment(int start, int length, string? source)
            : this(start, length, source, 0, 1, 1, 1) { }
        public WmlSourceSegment(int start, int length, string? source, int sourceStart, int sourceLine, int sourceColumn, int textLine)
        {
            Start = start;
            Length = length;
            Source = source;
            SourceStart = sourceStart;
            SourceLine = sourceLine;
            SourceColumn = sourceColumn;
            TextLine = textLine;
        }

        public int Start { get; }
        public int Length { get; }
        public string? Source { get; }
        public int SourceStart { get; }
        public int SourceLine { get; }
        public int SourceColumn { get; }
        public int TextLine { get; }
    }

    public interface IWmlSourceResolver
    {
        Task<WmlSource?> ResolveAsync(string path, string? includingSource, CancellationToken cancellationToken);
        Task<bool> ExistsAsync(string path, string? includingSource, CancellationToken cancellationToken);
    }

    public sealed class WmlPreprocessorOptions
    {
        public WmlPreprocessorOptions()
        {
            Defines["WESNOTH_VERSION"] = "1.18.7";
            Defines["WESNOTH_VERSION_MAJOR"] = "1"; Defines["WESNOTH_VERSION_MINOR"] = "18"; Defines["WESNOTH_VERSION_REVISION"] = "7";
        }
        public IDictionary<string, string> Defines { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public IWmlSourceResolver? SourceResolver { get; set; }
        public string? CoreDirectory { get; set; }
        public int MaxIncludeDepth { get; set; } = 64;
        public int MaxMacroExpansionDepth { get; set; } = 256;
        public long MaxOutputBytes { get; set; } = 128L * 1024 * 1024;
    }

    public sealed class WmlMacroDefinition
    {
        internal WmlMacroDefinition(string name, IReadOnlyList<string> parameters, string body, string? source, WmlSourceReference? definition, IReadOnlyDictionary<string, string>? optionalArguments = null) { Name = name; Parameters = parameters; Body = body; Source = source; Definition = definition ?? WmlSourceReference.FileOnly(source); OptionalArguments = optionalArguments ?? new Dictionary<string, string>(); }
        public string Name { get; }
        public IReadOnlyList<string> Parameters { get; }
        public string Body { get; }
        public string? Source { get; }
        public WmlSourceReference Definition { get; }
        public WmlSourceSpan? DefinitionSpan => Definition.Span;
        public IReadOnlyDictionary<string, string> OptionalArguments { get; }
    }

    public sealed class WmlSourceMapEntry
    {
        public WmlSourceMapEntry(int outputStart, int outputLength, string? source)
            : this(outputStart, outputLength, source, source == null ? null : new WmlExpansionProvenance(WmlSourceReference.FileOnly(source))) { }
        public WmlSourceMapEntry(int outputStart, int outputLength, string? source, WmlExpansionProvenance? provenance) { OutputStart = outputStart; OutputLength = outputLength; Source = source ?? provenance?.LogicalSource; Provenance = provenance; }
        public int OutputStart { get; } public int OutputLength { get; } public string? Source { get; }
        public WmlExpansionProvenance? Provenance { get; }
        public WmlSourceSpan? SourceSpan => Provenance?.SourceSpan;
    }

    public sealed class WmlPreprocessorResult
    {
        internal WmlPreprocessorResult(WmlSyntaxTree syntax, IReadOnlyList<WmlDiagnostic> diagnostics, IReadOnlyDictionary<string, WmlMacroDefinition> macros, IReadOnlyList<WmlSourceMapEntry> sourceMap)
        { Syntax = syntax; Diagnostics = diagnostics; Macros = macros; SourceMap = sourceMap; }
        public WmlSyntaxTree Syntax { get; }
        public string Text => Syntax.Text;
        public IReadOnlyList<WmlDiagnostic> Diagnostics { get; }
        public IReadOnlyDictionary<string, WmlMacroDefinition> Macros { get; }
        public IReadOnlyList<WmlSourceMapEntry> SourceMap { get; }
        public bool HasErrors => Diagnostics.Any(d => d.Severity == WmlDiagnosticSeverity.Error) || Syntax.HasErrors;
    }

    public static class WmlPreprocessor
    {
        public static Task<WmlPreprocessorResult> ProcessAsync(string text, WmlPreprocessorOptions? options = null, string? sourceName = null, CancellationToken cancellationToken = default(CancellationToken))
            => new Processor(options ?? new WmlPreprocessorOptions(), cancellationToken).RunAsync(text ?? string.Empty, sourceName);

        private sealed class Processor
        {
            private readonly WmlPreprocessorOptions _options; private readonly CancellationToken _cancellation; private readonly IWmlSourceResolver? _resolver;
            private readonly Dictionary<string, WmlMacroDefinition> _macros = new Dictionary<string, WmlMacroDefinition>(StringComparer.Ordinal);
            private readonly List<WmlDiagnostic> _diagnostics = new List<WmlDiagnostic>(); private readonly List<WmlSourceMapEntry> _map = new List<WmlSourceMapEntry>();
            private readonly HashSet<string> _includes = new HashSet<string>(StringComparer.OrdinalIgnoreCase); private long _bytes;
            internal Processor(WmlPreprocessorOptions options, CancellationToken cancellation) { _options = options; _cancellation = cancellation; _resolver = options.SourceResolver ?? (options.CoreDirectory == null ? null : new FileSystemWmlSourceResolver(options.CoreDirectory)); foreach (var d in options.Defines) _macros[d.Key] = new WmlMacroDefinition(d.Key, new string[0], d.Value, null, WmlSourceReference.Unknown); }
            internal async Task<WmlPreprocessorResult> RunAsync(string text, string? source)
            {
                if (source != null) _includes.Add(source); string expanded = await ProcessTextAsync(text, ProcessingContext.ForExactSource(source), 0, 0).ConfigureAwait(false); var syntax = AttachProvenance(WmlParser.Parse(expanded, source));
                return new WmlPreprocessorResult(syntax, _diagnostics, _macros, _map);
            }

            private async Task<string> ProcessTextAsync(string text, ProcessingContext context, int includeDepth, int macroDepth)
            {
                _cancellation.ThrowIfCancellationRequested();
                if (includeDepth > _options.MaxIncludeDepth) { Error("WML2001", "Maximum include depth exceeded.", context.FallbackProvenance); return string.Empty; }
                if (macroDepth > _options.MaxMacroExpansionDepth) { Error("WML2002", "Maximum macro expansion depth exceeded.", context.FallbackProvenance); return string.Empty; }
                var output = new StringBuilder(); var lines = ReadLines(text); var active = new Stack<Condition>(); bool enabled = true, strongQuoted = false, quoted = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    _cancellation.ThrowIfCancellationRequested(); var sourceLine = lines[i]; string line = sourceLine.Text; string trimmed = line.Trim(); bool directiveAllowed = !strongQuoted && !quoted;
                    if (directiveAllowed && trimmed.StartsWith("#define ", StringComparison.Ordinal))
                    {
                        var header = Tokens(trimmed.Substring(8)); string name = header.Count == 0 ? "" : header[0]; var parameters = header.Skip(1).ToList(); var body = new StringBuilder();
                        int defineOffset = Math.Max(0, line.IndexOf("#define", StringComparison.Ordinal)); bool definitionQuote = false, definitionRaw = false; WmlSourceReference definition = ReferenceFor(context, sourceLine, defineOffset, Math.Max(7, trimmed.Length));
                        while (++i < lines.Count)
                        {
                            var definitionSourceLine = lines[i]; string definitionLine = definitionSourceLine.Text; int enddef = FindDirective(definitionLine, "#enddef", ref definitionQuote, ref definitionRaw);
                            if (enddef < 0) { body.Append(definitionLine); continue; }
                            body.Append(definitionLine, 0, enddef);
                            definition = DefinitionReference(context, sourceLine, defineOffset, definitionSourceLine, enddef + 7);
                            string remainder = definitionLine.Substring(enddef + 7);
                            if (remainder.Trim().Length > 0) lines.Insert(i + 1, definitionSourceLine.Slice(enddef + 7));
                            break;
                        }
                        var optional = new Dictionary<string, string>(StringComparer.Ordinal); string macroBody = ExtractOptionalArguments(body.ToString(), optional);
                        if (enabled && name.Length > 0) _macros[name] = new WmlMacroDefinition(name, parameters, macroBody, context.Source, definition, optional); continue;
                    }
                    if (directiveAllowed && (trimmed.StartsWith("#ifdef ", StringComparison.Ordinal) || trimmed.StartsWith("#ifndef ", StringComparison.Ordinal) || trimmed.StartsWith("#ifhave ", StringComparison.Ordinal) || trimmed.StartsWith("#ifnhave ", StringComparison.Ordinal) || trimmed.StartsWith("#ifver ", StringComparison.Ordinal) || trimmed.StartsWith("#ifnver ", StringComparison.Ordinal)))
                    {
                        var provenance = ProvenanceFor(context, sourceLine, Math.Max(0, line.IndexOf(trimmed, StringComparison.Ordinal)), trimmed.Length);
                        bool result = await EvaluateCondition(trimmed, context.Source).ConfigureAwait(false); active.Push(new Condition(enabled, result, provenance)); enabled = enabled && result; continue;
                    }
                    if (directiveAllowed && trimmed.StartsWith("#else", StringComparison.Ordinal)) { var provenance = ProvenanceFor(context, sourceLine, Math.Max(0, line.IndexOf("#else", StringComparison.Ordinal)), 5); if (active.Count == 0) Error("WML2003", "#else without matching conditional.", provenance); else { var c = active.Pop(); c = new Condition(c.ParentEnabled, !c.Branch, c.Provenance); active.Push(c); enabled = c.ParentEnabled && c.Branch; } continue; }
                    if (directiveAllowed && trimmed.StartsWith("#endif", StringComparison.Ordinal)) { var provenance = ProvenanceFor(context, sourceLine, Math.Max(0, line.IndexOf("#endif", StringComparison.Ordinal)), 6); if (active.Count == 0) Error("WML2004", "#endif without matching conditional.", provenance); else { var c = active.Pop(); enabled = c.ParentEnabled; } continue; }
                    if (directiveAllowed && TryFindInlineConditionalDirective(line, out int inlineDirective, out string directive))
                    {
                        string prefix = line.Substring(0, inlineDirective);
                        if (enabled && prefix.Trim().Length > 0)
                        {
                            string ending = line.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : line.EndsWith("\n", StringComparison.Ordinal) ? "\n" : string.Empty;
                            int inlineMapStart = _map.Count; var inlineExpansion = await ExpandCallsAsync(prefix + ending, strongQuoted, context, sourceLine, includeDepth, macroDepth).ConfigureAwait(false);
                            strongQuoted = inlineExpansion.StrongQuoted; ShiftMapEntries(inlineMapStart, output.Length); Append(output, inlineExpansion.Text, ProvenanceFor(context, sourceLine, 0, prefix.Length + ending.Length));
                        }
                        quoted = false; var directiveProvenance = ProvenanceFor(context, sourceLine, inlineDirective, directive.Length);
                        if (directive == "#else") { if (active.Count == 0) Error("WML2003", "#else without matching conditional.", directiveProvenance); else { var c = active.Pop(); c = new Condition(c.ParentEnabled, !c.Branch, c.Provenance); active.Push(c); enabled = c.ParentEnabled && c.Branch; } }
                        else { if (active.Count == 0) Error("WML2004", "#endif without matching conditional.", directiveProvenance); else { var c = active.Pop(); enabled = c.ParentEnabled; } }
                        string remainder = line.Substring(inlineDirective + directive.Length);
                        if (remainder.Trim().Length > 0) lines.Insert(i + 1, sourceLine.Slice(inlineDirective + directive.Length));
                        continue;
                    }
                    if (!enabled) continue;
                    if (directiveAllowed && trimmed.StartsWith("#undef ", StringComparison.Ordinal)) { _macros.Remove(Tokens(trimmed.Substring(7)).FirstOrDefault() ?? ""); continue; }
                    if (directiveAllowed && trimmed.StartsWith("#warning", StringComparison.Ordinal)) { Report("WML2005", trimmed.Substring(8).Trim(), WmlDiagnosticSeverity.Warning, ProvenanceFor(context, sourceLine, Math.Max(0, line.IndexOf("#warning", StringComparison.Ordinal)), trimmed.Length)); continue; }
                    if (directiveAllowed && trimmed.StartsWith("#error", StringComparison.Ordinal)) { Error("WML2006", trimmed.Substring(6).Trim(), ProvenanceFor(context, sourceLine, Math.Max(0, line.IndexOf("#error", StringComparison.Ordinal)), trimmed.Length)); continue; }
                    if (directiveAllowed && trimmed.StartsWith("#deprecated", StringComparison.Ordinal)) { Report("WML2007", trimmed.Substring(11).Trim(), WmlDiagnosticSeverity.Warning, ProvenanceFor(context, sourceLine, Math.Max(0, line.IndexOf("#deprecated", StringComparison.Ordinal)), trimmed.Length)); continue; }
                    if (directiveAllowed && trimmed.StartsWith("#", StringComparison.Ordinal) && !trimmed.StartsWith("#textdomain", StringComparison.Ordinal)) continue;
                    int comment = FindComment(line, strongQuoted, ref quoted); if (comment >= 0) line = line.Substring(0, comment) + (line.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : line.EndsWith("\n", StringComparison.Ordinal) ? "\n" : string.Empty);
                    while (HasUnterminatedExpression(line, strongQuoted) && i + 1 < lines.Count)
                    {
                        string continuation = lines[++i].Text;
                        int continuationComment = FindComment(continuation, strongQuoted, ref quoted); if (continuationComment >= 0) continuation = continuation.Substring(0, continuationComment) + (continuation.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : continuation.EndsWith("\n", StringComparison.Ordinal) ? "\n" : string.Empty);
                        line += continuation;
                    }
                    int mapStart = _map.Count; var expansion = await ExpandCallsAsync(line, strongQuoted, context, sourceLine, includeDepth, macroDepth).ConfigureAwait(false);
                    strongQuoted = expansion.StrongQuoted; ShiftMapEntries(mapStart, output.Length); Append(output, expansion.Text, ProvenanceFor(context, sourceLine, 0, line.Length));
                }
                if (active.Count > 0) Error("WML2008", "Unterminated conditional block.", active.Peek().Provenance); return output.ToString();
            }

            private async Task<ExpansionResult> ExpandCallsAsync(string line, bool strongQuoted, ProcessingContext context, SourceLine sourceLine, int includeDepth, int macroDepth)
            {
                var output = new StringBuilder(); int position = 0;
                while (position < line.Length)
                {
                    if (strongQuoted)
                    {
                        int rawClose = line.IndexOf(">>", position, StringComparison.Ordinal);
                        if (rawClose < 0) { output.Append(line, position, line.Length - position); position = line.Length; break; }
                        output.Append(line, position, rawClose + 2 - position); position = rawClose + 2; strongQuoted = false; continue;
                    }
                    int rawOpen = line.IndexOf("<<", position, StringComparison.Ordinal); int open = line.IndexOf('{', position);
                    if (rawOpen >= 0 && (open < 0 || rawOpen < open)) { output.Append(line, position, rawOpen + 2 - position); position = rawOpen + 2; strongQuoted = true; continue; }
                    if (open < 0) { output.Append(line, position, line.Length - position); break; }
                    output.Append(line, position, open - position); int close = MatchingBrace(line, open); var invocationProvenance = ProvenanceFor(context, sourceLine, open, close < 0 ? line.Length - open : close - open + 1); if (close < 0) { output.Append(line, open, line.Length - open); Error("WML2009", "Unterminated macro/include expression.", invocationProvenance); break; }
                    string expression = line.Substring(open + 1, close - open - 1); var tokens = Tokens(expression); string replacement = string.Empty;
                    int mapStart = _map.Count;
                    if (tokens.Count > 0 && _macros.TryGetValue(tokens[0], out var macro)) replacement = await ExpandMacroAsync(macro, tokens.Skip(1).ToList(), invocationProvenance, includeDepth, macroDepth + 1).ConfigureAwait(false);
                    else if (tokens.Count > 0)
                    {
                        string candidate = expression.Trim();
                        var kind = tokens.Count > 1 && IsSymbol(tokens[0]) ? WmlPreprocessorExpressionKind.MacroInvocation : tokens.Count == 1 && IsSymbol(tokens[0]) ? WmlPreprocessorExpressionKind.Ambiguous : WmlPreprocessorExpressionKind.Include;
                        replacement = await IncludeAsync(expression, candidate, tokens[0], kind, invocationProvenance, context, includeDepth + 1, macroDepth).ConfigureAwait(false);
                    }
                    ShiftMapEntries(mapStart, output.Length);
                    output.Append(replacement); position = close + 1;
                }
                return new ExpansionResult(output.ToString(), strongQuoted);
            }

            private async Task<string> ExpandMacroAsync(WmlMacroDefinition macro, List<string> arguments, WmlExpansionProvenance invocation, int includeDepth, int depth)
            {
                if (depth > _options.MaxMacroExpansionDepth) { Error("WML2002", "Maximum macro expansion depth exceeded.", invocation); return string.Empty; }
                string body = macro.Body; var values = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var item in macro.OptionalArguments) values[item.Key] = item.Value; int positional = 0; var explicitlyNamed = new HashSet<string>(StringComparer.Ordinal);
                foreach (string argument in arguments)
                {
                    string value = argument;
                    if (TryUnwrapGroupedArgument(argument, out var grouped))
                    {
                        if (TryBindNamedArgument(grouped, macro, values, explicitlyNamed)) continue;
                        value = grouped;
                    }
                    else if (TryBindNamedArgument(argument, macro, values, explicitlyNamed)) continue;
                    while (positional < macro.Parameters.Count && explicitlyNamed.Contains(macro.Parameters[positional])) positional++;
                    if (positional < macro.Parameters.Count) values[macro.Parameters[positional++]] = value;
                }
                foreach (string parameter in macro.Parameters) if (!values.ContainsKey(parameter)) values[parameter] = string.Empty;
                ResolveSubstitutionValues(values);
                foreach (var pair in values) body = body.Replace("{" + pair.Key + "}", pair.Value);
                return await ProcessTextAsync(body, ProcessingContext.ForMacro(macro, invocation), includeDepth, depth).ConfigureAwait(false);
            }

            private async Task<string> IncludeAsync(string expression, string path, string symbol, WmlPreprocessorExpressionKind kind, WmlExpansionProvenance invocation, ProcessingContext context, int includeDepth, int macroDepth)
            {
                if (_resolver == null)
                {
                    if (kind == WmlPreprocessorExpressionKind.MacroInvocation) Error("WML2014", $"Unknown macro '{symbol}'; include fallback '{path}' did not run because no source resolver is configured.", invocation, Context(false));
                    else Error("WML2010", $"No source resolver is configured for include '{path}'.", invocation, Context(false));
                    return string.Empty;
                }
                WmlSource? resolved;
                try { resolved = await _resolver.ResolveAsync(path, invocation.LogicalSource ?? context.Source, _cancellation).ConfigureAwait(false); }
                catch (Exception ex) { FailedFallback($"failed: {ex.Message}"); return string.Empty; }
                if (resolved == null) { FailedFallback("was not found."); return string.Empty; }
                if (!_includes.Add(resolved.Path)) { Error("WML2013", $"Include cycle detected at '{resolved.Path}'.", invocation, Context(true)); return string.Empty; }
                try { return await ProcessTextAsync(resolved.Text, ProcessingContext.ForIncludedSource(resolved, context.ExpansionChain), includeDepth, macroDepth).ConfigureAwait(false); }
                finally { _includes.Remove(resolved.Path); }

                WmlPreprocessorDiagnosticContext Context(bool attempted) => new WmlPreprocessorDiagnosticContext(expression, symbol, kind, false, attempted, attempted ? path : null);
                void FailedFallback(string outcome)
                {
                    if (kind == WmlPreprocessorExpressionKind.MacroInvocation) Error("WML2014", $"Unknown macro '{symbol}'; include fallback '{path}' {outcome}", invocation, Context(true));
                    else Error(outcome.StartsWith("failed:", StringComparison.Ordinal) ? "WML2011" : "WML2012", $"Include '{path}' {outcome}", invocation, Context(true));
                }
            }

            private async Task<bool> EvaluateCondition(string line, string? source)
            {
                var tokens = Tokens(line); if (tokens.Count < 2) return false; string op = tokens[0]; string value = tokens[1];
                if (op == "#ifdef" || op == "#ifndef") return _macros.ContainsKey(value) == (op == "#ifdef");
                if (op == "#ifhave" || op == "#ifnhave") { bool exists = _resolver != null && await _resolver.ExistsAsync(value, source, _cancellation).ConfigureAwait(false); return exists == (op == "#ifhave"); }
                if (op == "#ifver" || op == "#ifnver")
                {
                    string actual = _macros.TryGetValue(value, out var m) ? m.Body.Trim() : value; string compare = tokens.Count > 2 ? tokens[2] : ">="; string expected = tokens.Count > 3 ? tokens[3] : "0"; int c = CompareVersions(actual, expected);
                    bool result = compare == ">" ? c > 0 : compare == ">=" ? c >= 0 : compare == "<" ? c < 0 : compare == "<=" ? c <= 0 : compare == "!=" ? c != 0 : c == 0; return result == (op == "#ifver");
                }
                return false;
            }

            private WmlSyntaxTree AttachProvenance(WmlSyntaxTree syntax)
            {
                ApplyProvenance(syntax.Document);
                var diagnostics = syntax.Diagnostics.Select(d => new WmlDiagnostic(d.Code, d.Message, d.Severity, d.Span, d.PreprocessorContext, d.Provenance ?? FindProvenance(d.Span?.Start ?? -1, d.Span?.Length ?? 0))).ToList();
                return new WmlSyntaxTree(syntax.Text, syntax.SourceName, syntax.Document, diagnostics);
            }

            private void ApplyProvenance(WmlNode node)
            {
                if (node.Span != null) node.Provenance = FindProvenance(node.Span.Start, node.Span.Length);
                if (node is WmlTag tag)
                {
                    if (tag.ClosingSpan != null) tag.ClosingProvenance = FindProvenance(tag.ClosingSpan.Start, tag.ClosingSpan.Length);
                    foreach (var child in tag.Children) ApplyProvenance(child);
                }
                else if (node is WmlDocument document)
                {
                    foreach (var child in document.Children) ApplyProvenance(child);
                }
            }

            private WmlExpansionProvenance? FindProvenance(int outputStart, int outputLength)
            {
                if (outputStart < 0) return null;
                int end = outputStart + Math.Max(1, outputLength); WmlSourceMapEntry? best = null;
                foreach (var entry in _map)
                {
                    if (entry.Provenance == null || entry.OutputLength <= 0) continue;
                    bool overlaps = entry.OutputStart < end && entry.OutputStart + entry.OutputLength > outputStart;
                    if (!overlaps) continue;
                    if (best == null || entry.Provenance.ExpansionChain.Count > best.Provenance!.ExpansionChain.Count || (entry.Provenance.ExpansionChain.Count == best.Provenance.ExpansionChain.Count && entry.OutputLength < best.OutputLength)) best = entry;
                }
                return best?.Provenance;
            }

            private void Append(StringBuilder output, string value, WmlExpansionProvenance provenance) { long bytes = Encoding.UTF8.GetByteCount(value); _bytes += bytes; if (_bytes > _options.MaxOutputBytes) throw new WmlException("Maximum preprocessor output size exceeded."); int start = output.Length; output.Append(value); if (value.Length > 0) _map.Add(new WmlSourceMapEntry(start, value.Length, provenance.LogicalSource, provenance)); }
            private void ShiftMapEntries(int start, int offset) { for (int i = start; i < _map.Count; i++) { var entry = _map[i]; _map[i] = new WmlSourceMapEntry(entry.OutputStart + offset, entry.OutputLength, entry.Source, entry.Provenance); } }
            private void Error(string code, string message, WmlExpansionProvenance provenance, WmlPreprocessorDiagnosticContext? context = null) => Report(code, message, WmlDiagnosticSeverity.Error, provenance, context);
            private void Report(string code, string message, WmlDiagnosticSeverity severity, WmlExpansionProvenance provenance, WmlPreprocessorDiagnosticContext? context = null) => _diagnostics.Add(new WmlDiagnostic(code, message, severity, DiagnosticSpan(provenance), context, provenance));
            private static WmlSourceSpan DiagnosticSpan(WmlExpansionProvenance provenance) => provenance.SourceSpan ?? new WmlSourceSpan(provenance.LogicalSource, 0, 0, 1, 1);
            private WmlExpansionProvenance ProvenanceFor(ProcessingContext context, SourceLine line, int offset, int length) => new WmlExpansionProvenance(ReferenceFor(context, line, offset, length), context.ExpansionChain);
            private static WmlSourceReference ReferenceFor(ProcessingContext context, SourceLine line, int offset, int length) => context.ReferenceFor(line, offset, length);
            private static WmlSourceReference DefinitionReference(ProcessingContext context, SourceLine startLine, int startOffset, SourceLine endLine, int endOffset)
            {
                if (!context.ExactLineSpans) return context.FallbackReference;
                int absoluteStart = startLine.Start + Math.Max(0, startOffset); int absoluteEnd = endLine.Start + Math.Max(0, endOffset); var start = context.ReferenceFor(startLine, startOffset, 0);
                return start.Precision == WmlSourceReferencePrecision.Exact ? WmlSourceReference.Exact(new WmlSourceSpan(start.Source, start.Start, Math.Max(0, absoluteEnd - absoluteStart), start.Line, start.Column)) : start;
            }

            private sealed class ProcessingContext
            {
                private ProcessingContext(string? source, bool exactLineSpans, WmlSourceReference fallbackReference, IReadOnlyList<WmlMacroExpansionFrame> expansionChain, IReadOnlyList<WmlSourceSegment>? segments = null)
                { Source = source; ExactLineSpans = exactLineSpans; FallbackReference = fallbackReference; ExpansionChain = expansionChain; Segments = segments ?? Array.Empty<WmlSourceSegment>(); FallbackProvenance = new WmlExpansionProvenance(fallbackReference, expansionChain); }
                public string? Source { get; }
                public bool ExactLineSpans { get; }
                public WmlSourceReference FallbackReference { get; }
                public IReadOnlyList<WmlMacroExpansionFrame> ExpansionChain { get; }
                public IReadOnlyList<WmlSourceSegment> Segments { get; }
                public WmlExpansionProvenance FallbackProvenance { get; }
                public static ProcessingContext ForExactSource(string? source) => new ProcessingContext(source, true, WmlSourceReference.FileOnly(source), Array.Empty<WmlMacroExpansionFrame>());
                public static ProcessingContext ForIncludedSource(WmlSource source, IReadOnlyList<WmlMacroExpansionFrame> expansionChain) => new ProcessingContext(source.Path, true, WmlSourceReference.FileOnly(source.Path), expansionChain, source.Segments);
                public static ProcessingContext ForMacro(WmlMacroDefinition macro, WmlExpansionProvenance invocation)
                {
                    var chain = invocation.ExpansionChain.Concat(new[] { new WmlMacroExpansionFrame(macro.Name, macro.Definition, invocation.Source) }).ToList();
                    return new ProcessingContext(macro.Source ?? invocation.LogicalSource, false, macro.Definition, chain);
                }
                public WmlSourceReference ReferenceFor(SourceLine line, int offset, int length)
                {
                    if (!ExactLineSpans) return FallbackReference;
                    int safeOffset = Math.Max(0, offset); int absolute = line.Start + safeOffset; int safeLength = Math.Max(0, length);
                    var segment = Segments.LastOrDefault(item => item.Start <= absolute && absolute <= item.Start + item.Length);
                    if (segment == null) return WmlSourceReference.Exact(new WmlSourceSpan(Source, absolute, safeLength, line.Line, line.Column + safeOffset));
                    int sourceStart = segment.SourceStart + Math.Max(0, absolute - segment.Start);
                    int sourceLine = segment.SourceLine + Math.Max(0, line.Line - segment.TextLine);
                    int sourceColumn = line.Line == segment.TextLine ? segment.SourceColumn + line.Column + safeOffset - 1 : line.Column + safeOffset;
                    return WmlSourceReference.Exact(new WmlSourceSpan(segment.Source, sourceStart, safeLength, sourceLine, sourceColumn));
                }
            }

            private sealed class SourceLine
            {
                public SourceLine(string text, int start, int line, int column) { Text = text; Start = start; Line = line; Column = column; }
                public string Text { get; }
                public int Start { get; }
                public int Line { get; }
                public int Column { get; }
                public SourceLine Slice(int offset) => new SourceLine(Text.Substring(offset), Start + offset, Line, Column + offset);
            }

            private readonly struct Condition { public Condition(bool parentEnabled, bool branch, WmlExpansionProvenance provenance) { ParentEnabled = parentEnabled; Branch = branch; Provenance = provenance; } public bool ParentEnabled { get; } public bool Branch { get; } public WmlExpansionProvenance Provenance { get; } }
            private readonly struct ExpansionResult { public ExpansionResult(string text, bool strongQuoted) { Text = text; StrongQuoted = strongQuoted; } public string Text { get; } public bool StrongQuoted { get; } }
            private static List<SourceLine> ReadLines(string text) { var result = new List<SourceLine>(); int start = 0, line = 1; for (int i = 0; i < text.Length; i++) if (text[i] == '\n') { result.Add(new SourceLine(text.Substring(start, i - start + 1), start, line++, 1)); start = i + 1; } if (start < text.Length) result.Add(new SourceLine(text.Substring(start), start, line, 1)); return result; }
            private static int FindComment(string text, bool strongQuoted, ref bool quote) { for (int i = 0; i < text.Length; i++) { if (strongQuoted) { if (i + 1 < text.Length && text[i] == '>' && text[i + 1] == '>') { strongQuoted = false; i++; } continue; } if (!quote && i + 1 < text.Length && text[i] == '<' && text[i + 1] == '<') { strongQuoted = true; i++; continue; } if (text[i] == '"') { if (quote && i + 1 < text.Length && text[i + 1] == '"') i++; else quote = !quote; } else if (!quote && text[i] == '#') return i; } return -1; }
            private static bool HasUnterminatedExpression(string text, bool strongQuoted) { int depth = 0; for (int i = 0; i < text.Length; i++) { if (strongQuoted) { if (i + 1 < text.Length && text[i] == '>' && text[i + 1] == '>') { strongQuoted = false; i++; } continue; } if (i + 1 < text.Length && text[i] == '<' && text[i + 1] == '<') { strongQuoted = true; i++; continue; } if (text[i] == '{') depth++; else if (text[i] == '}' && depth > 0) depth--; } return depth > 0; }
            private static int MatchingBrace(string s, int open) { int depth = 0; bool raw = false; for (int i = open; i < s.Length; i++) { if (!raw && i + 1 < s.Length && s[i] == '<' && s[i + 1] == '<') { raw = true; i++; continue; } if (raw && i + 1 < s.Length && s[i] == '>' && s[i + 1] == '>') { raw = false; i++; continue; } if (raw) continue; if (s[i] == '{') depth++; else if (s[i] == '}' && --depth == 0) return i; } return -1; }
            private static int FindDirective(string text, string directive, ref bool quote, ref bool raw) { for (int i = 0; i < text.Length; i++) { if (!quote && !raw && i + 1 < text.Length && text[i] == '<' && text[i + 1] == '<') { raw = true; i++; continue; } if (raw && i + 1 < text.Length && text[i] == '>' && text[i + 1] == '>') { raw = false; i++; continue; } if (!raw && text[i] == '"') { quote = !quote; continue; } if (!quote && !raw && i <= text.Length - directive.Length && string.CompareOrdinal(text, i, directive, 0, directive.Length) == 0 && (i + directive.Length == text.Length || char.IsWhiteSpace(text[i + directive.Length]) || text[i + directive.Length] == '#')) return i; } return -1; }
            private static bool TryFindInlineConditionalDirective(string line, out int index, out string directive)
            {
                bool quote = false, raw = false;
                for (int i = 0; i < line.Length; i++)
                {
                    if (!quote && !raw && i + 1 < line.Length && line[i] == '<' && line[i + 1] == '<') { raw = true; i++; continue; }
                    if (raw && i + 1 < line.Length && line[i] == '>' && line[i + 1] == '>') { raw = false; i++; continue; }
                    if (!raw && line[i] == '"') { quote = !quote; continue; }
                    if (quote || raw || line[i] != '#') continue;

                    index = -1; directive = string.Empty;
                    if (i == 0) return false;
                    if (MatchesConditionalDirective(line, i, "#else")) { index = i; directive = "#else"; return true; }
                    if (MatchesConditionalDirective(line, i, "#endif")) { index = i; directive = "#endif"; return true; }
                    return false;
                }
                index = -1; directive = string.Empty; return false;
            }
            private static bool MatchesConditionalDirective(string line, int index, string directive) => index <= line.Length - directive.Length && string.CompareOrdinal(line, index, directive, 0, directive.Length) == 0 && (index + directive.Length == line.Length || char.IsWhiteSpace(line[index + directive.Length]) || line[index + directive.Length] == '#');
            private static bool IsSymbol(string value) { if (value.Length == 0) return false; foreach (char c in value) if (!(char.IsLetterOrDigit(c) || c == '_')) return false; return true; }
            private static bool TryUnwrapGroupedArgument(string value, out string grouped) { grouped = string.Empty; if (value.Length < 2 || value[0] != '(' || value[value.Length - 1] != ')') return false; int depth = 0; bool quote = false, raw = false; for (int i = 0; i < value.Length; i++) { if (!quote && !raw && i + 1 < value.Length && value[i] == '<' && value[i + 1] == '<') { raw = true; i++; continue; } if (raw && i + 1 < value.Length && value[i] == '>' && value[i + 1] == '>') { raw = false; i++; continue; } if (!raw && value[i] == '"') { quote = !quote; continue; } if (quote || raw) continue; if (value[i] == '(') depth++; else if (value[i] == ')' && --depth == 0 && i != value.Length - 1) return false; } if (depth != 0) return false; grouped = value.Substring(1, value.Length - 2); return true; }
            private static int FindArgumentEquals(string value) { int parentheses = 0, braces = 0; bool quote = false, raw = false; for (int i = 0; i < value.Length; i++) { if (!quote && !raw && i + 1 < value.Length && value[i] == '<' && value[i + 1] == '<') { raw = true; i++; continue; } if (raw && i + 1 < value.Length && value[i] == '>' && value[i + 1] == '>') { raw = false; i++; continue; } if (!raw && value[i] == '"') { quote = !quote; continue; } if (quote || raw) continue; if (value[i] == '(') parentheses++; else if (value[i] == ')' && parentheses > 0) parentheses--; else if (value[i] == '{') braces++; else if (value[i] == '}' && braces > 0) braces--; else if (value[i] == '=' && parentheses == 0 && braces == 0) return i; } return -1; }
            private static bool TryBindNamedArgument(string argument, WmlMacroDefinition macro, IDictionary<string, string> values, ISet<string> explicitlyNamed)
            {
                int equals = FindArgumentEquals(argument); string candidate = equals > 0 ? argument.Substring(0, equals).Trim() : string.Empty;
                if (equals <= 0 || !IsSymbol(candidate) || (!macro.Parameters.Contains(candidate) && !macro.OptionalArguments.ContainsKey(candidate))) return false;
                values[candidate] = argument.Substring(equals + 1); explicitlyNamed.Add(candidate); return true;
            }
            private static void ResolveSubstitutionValues(IDictionary<string, string> values)
            {
                var keys = values.Keys.ToList();
                for (int pass = 0; pass < keys.Count; pass++)
                {
                    bool changed = false;
                    foreach (string key in keys)
                    {
                        string value = values[key];
                        foreach (string replacement in keys) value = value.Replace("{" + replacement + "}", values[replacement]);
                        if (!string.Equals(value, values[key], StringComparison.Ordinal)) { values[key] = value; changed = true; }
                    }
                    if (!changed) break;
                }
            }
            private static List<string> Tokens(string input) { var result = new List<string>(); var b = new StringBuilder(); int paren = 0, braces = 0; bool quote = false, raw = false; for (int i = 0; i < input.Length; i++) { char c = input[i]; if (!quote && !raw && paren == 0 && braces == 0 && c == '(' && b.Length > 0 && b[b.Length - 1] == ')') { result.Add(b.ToString()); b.Clear(); } if (!quote && !raw && i + 1 < input.Length && c == '<' && input[i + 1] == '<') { raw = true; b.Append(c); b.Append(input[++i]); continue; } if (raw && i + 1 < input.Length && c == '>' && input[i + 1] == '>') { raw = false; b.Append(c); b.Append(input[++i]); continue; } if (!raw && c == '"') quote = !quote; if (!quote && !raw && c == '(') paren++; if (!quote && !raw && c == ')') paren--; if (!quote && !raw && c == '{') braces++; if (!quote && !raw && c == '}') braces--; if (!quote && !raw && paren == 0 && braces == 0 && char.IsWhiteSpace(c)) { if (b.Length > 0) { result.Add(b.ToString()); b.Clear(); } } else b.Append(c); } if (b.Length > 0) result.Add(b.ToString()); return result; }
            private static string ExtractOptionalArguments(string body, IDictionary<string, string> optional)
            {
                var lines = ReadLines(body); var output = new StringBuilder();
                for (int i = 0; i < lines.Count; i++)
                {
                    string trimmed = lines[i].Text.Trim(); if (!trimmed.StartsWith("#arg ", StringComparison.Ordinal)) { output.Append(lines[i].Text); continue; }
                    string name = Tokens(trimmed.Substring(5)).FirstOrDefault() ?? ""; var value = new StringBuilder(); bool quote = false, raw = false;
                    while (++i < lines.Count)
                    {
                        string valueLine = lines[i].Text; int endarg = FindDirective(valueLine, "#endarg", ref quote, ref raw);
                        if (endarg < 0) { value.Append(valueLine); continue; }
                        value.Append(valueLine, 0, endarg); break;
                    }
                    if (name.Length > 0) optional[name] = value.ToString();
                }
                return output.ToString();
            }
            private static int CompareVersions(string a, string b) { var aa = a.Split('.'); var bb = b.Split('.'); for (int i = 0; i < Math.Max(aa.Length, bb.Length); i++) { int av = i < aa.Length && int.TryParse(aa[i], out var x) ? x : 0; int bv = i < bb.Length && int.TryParse(bb[i], out var y) ? y : 0; int c = av.CompareTo(bv); if (c != 0) return c; } return 0; }
        }
    }
}
