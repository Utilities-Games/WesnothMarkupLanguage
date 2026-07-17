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
        public WmlSource(string path, string text) { Path = path; Text = text; }
        public string Path { get; }
        public string Text { get; }
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
        internal WmlMacroDefinition(string name, IReadOnlyList<string> parameters, string body, string? source, IReadOnlyDictionary<string, string>? optionalArguments = null) { Name = name; Parameters = parameters; Body = body; Source = source; OptionalArguments = optionalArguments ?? new Dictionary<string, string>(); }
        public string Name { get; }
        public IReadOnlyList<string> Parameters { get; }
        public string Body { get; }
        public string? Source { get; }
        public IReadOnlyDictionary<string, string> OptionalArguments { get; }
    }

    public sealed class WmlSourceMapEntry
    {
        public WmlSourceMapEntry(int outputStart, int outputLength, string? source) { OutputStart = outputStart; OutputLength = outputLength; Source = source; }
        public int OutputStart { get; } public int OutputLength { get; } public string? Source { get; }
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
            internal Processor(WmlPreprocessorOptions options, CancellationToken cancellation) { _options = options; _cancellation = cancellation; _resolver = options.SourceResolver ?? (options.CoreDirectory == null ? null : new FileSystemWmlSourceResolver(options.CoreDirectory)); foreach (var d in options.Defines) _macros[d.Key] = new WmlMacroDefinition(d.Key, new string[0], d.Value, null); }
            internal async Task<WmlPreprocessorResult> RunAsync(string text, string? source)
            {
                if (source != null) _includes.Add(source); string expanded = await ProcessTextAsync(text, source, 0, 0).ConfigureAwait(false); var syntax = WmlParser.Parse(expanded, source);
                return new WmlPreprocessorResult(syntax, _diagnostics, _macros, _map);
            }

            private async Task<string> ProcessTextAsync(string text, string? source, int includeDepth, int macroDepth)
            {
                _cancellation.ThrowIfCancellationRequested();
                if (includeDepth > _options.MaxIncludeDepth) { Error("WML2001", "Maximum include depth exceeded.", source); return string.Empty; }
                if (macroDepth > _options.MaxMacroExpansionDepth) { Error("WML2002", "Maximum macro expansion depth exceeded.", source); return string.Empty; }
                var output = new StringBuilder(); var lines = ReadLines(text); var active = new Stack<Condition>(); bool enabled = true;
                for (int i = 0; i < lines.Count; i++)
                {
                    _cancellation.ThrowIfCancellationRequested(); string line = lines[i]; string trimmed = line.Trim();
                    if (trimmed.StartsWith("#define ", StringComparison.Ordinal))
                    {
                        var header = Tokens(trimmed.Substring(8)); string name = header.Count == 0 ? "" : header[0]; var parameters = header.Skip(1).ToList(); var body = new StringBuilder();
                        bool definitionQuote = false, definitionRaw = false;
                        while (++i < lines.Count)
                        {
                            string definitionLine = lines[i]; int enddef = FindDirective(definitionLine, "#enddef", ref definitionQuote, ref definitionRaw);
                            if (enddef < 0) { body.Append(definitionLine); continue; }
                            body.Append(definitionLine, 0, enddef);
                            string remainder = definitionLine.Substring(enddef + 7);
                            if (remainder.Trim().Length > 0) lines.Insert(i + 1, remainder);
                            break;
                        }
                        var optional = new Dictionary<string, string>(StringComparer.Ordinal); string macroBody = ExtractOptionalArguments(body.ToString(), optional);
                        if (enabled && name.Length > 0) _macros[name] = new WmlMacroDefinition(name, parameters, macroBody, source, optional); continue;
                    }
                    if (trimmed.StartsWith("#ifdef ", StringComparison.Ordinal) || trimmed.StartsWith("#ifndef ", StringComparison.Ordinal) || trimmed.StartsWith("#ifhave ", StringComparison.Ordinal) || trimmed.StartsWith("#ifnhave ", StringComparison.Ordinal) || trimmed.StartsWith("#ifver ", StringComparison.Ordinal) || trimmed.StartsWith("#ifnver ", StringComparison.Ordinal))
                    {
                        bool result = await EvaluateCondition(trimmed, source).ConfigureAwait(false); active.Push(new Condition(enabled, result)); enabled = enabled && result; continue;
                    }
                    if (trimmed.StartsWith("#else", StringComparison.Ordinal)) { if (active.Count == 0) Error("WML2003", "#else without matching conditional.", source); else { var c = active.Pop(); c = new Condition(c.ParentEnabled, !c.Branch); active.Push(c); enabled = c.ParentEnabled && c.Branch; } continue; }
                    if (trimmed.StartsWith("#endif", StringComparison.Ordinal)) { if (active.Count == 0) Error("WML2004", "#endif without matching conditional.", source); else { var c = active.Pop(); enabled = c.ParentEnabled; } continue; }
                    if (!enabled) continue;
                    if (trimmed.StartsWith("#undef ", StringComparison.Ordinal)) { _macros.Remove(Tokens(trimmed.Substring(7)).FirstOrDefault() ?? ""); continue; }
                    if (trimmed.StartsWith("#warning", StringComparison.Ordinal)) { Report("WML2005", trimmed.Substring(8).Trim(), WmlDiagnosticSeverity.Warning, source); continue; }
                    if (trimmed.StartsWith("#error", StringComparison.Ordinal)) { Error("WML2006", trimmed.Substring(6).Trim(), source); continue; }
                    if (trimmed.StartsWith("#deprecated", StringComparison.Ordinal)) { Report("WML2007", trimmed.Substring(11).Trim(), WmlDiagnosticSeverity.Warning, source); continue; }
                    if (trimmed.StartsWith("#", StringComparison.Ordinal) && !trimmed.StartsWith("#textdomain", StringComparison.Ordinal)) continue;
                    int comment = WmlParsing.FindOutside(line, '#'); if (comment >= 0) line = line.Substring(0, comment) + (line.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : line.EndsWith("\n", StringComparison.Ordinal) ? "\n" : string.Empty);
                    if (FindCall(line, 0) >= 0 && MatchingBrace(line, FindCall(line, 0)) < 0) while (i + 1 < lines.Count && MatchingBrace(line, FindCall(line, 0)) < 0) line += lines[++i];
                    int mapStart = _map.Count; string expandedLine = await ExpandCallsAsync(line, source, includeDepth, macroDepth).ConfigureAwait(false);
                    ShiftMapEntries(mapStart, output.Length); Append(output, expandedLine, source);
                }
                if (active.Count > 0) Error("WML2008", "Unterminated conditional block.", source); return output.ToString();
            }

            private async Task<string> ExpandCallsAsync(string line, string? source, int includeDepth, int macroDepth)
            {
                var output = new StringBuilder(); int position = 0;
                while (position < line.Length)
                {
                    int open = FindCall(line, position); if (open < 0) { output.Append(line, position, line.Length - position); break; }
                    output.Append(line, position, open - position); int close = MatchingBrace(line, open); if (close < 0) { output.Append(line, open, line.Length - open); Error("WML2009", "Unterminated macro/include expression.", source); break; }
                    string expression = line.Substring(open + 1, close - open - 1); var tokens = Tokens(expression); string replacement = string.Empty;
                    int mapStart = _map.Count;
                    if (tokens.Count > 0 && _macros.TryGetValue(tokens[0], out var macro)) replacement = await ExpandMacroAsync(macro, tokens.Skip(1).ToList(), source, includeDepth, macroDepth + 1).ConfigureAwait(false);
                    else if (tokens.Count > 0)
                    {
                        string candidate = expression.Trim();
                        var kind = tokens.Count > 1 && IsSymbol(tokens[0]) ? WmlPreprocessorExpressionKind.MacroInvocation : tokens.Count == 1 && IsSymbol(tokens[0]) ? WmlPreprocessorExpressionKind.Ambiguous : WmlPreprocessorExpressionKind.Include;
                        replacement = await IncludeAsync(expression, candidate, tokens[0], kind, source, includeDepth + 1, macroDepth).ConfigureAwait(false);
                    }
                    ShiftMapEntries(mapStart, output.Length);
                    output.Append(replacement); position = close + 1;
                }
                return output.ToString();
            }

            private async Task<string> ExpandMacroAsync(WmlMacroDefinition macro, List<string> arguments, string? source, int includeDepth, int depth)
            {
                if (depth > _options.MaxMacroExpansionDepth) { Error("WML2002", "Maximum macro expansion depth exceeded.", source); return string.Empty; }
                string body = macro.Body; var values = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var item in macro.OptionalArguments) values[item.Key] = item.Value; int positional = 0;
                foreach (string argument in arguments)
                {
                    if (argument.StartsWith("(", StringComparison.Ordinal) && argument.EndsWith(")", StringComparison.Ordinal)) { string named = argument.Substring(1, argument.Length - 2); int equals = named.IndexOf('='); if (equals > 0) { values[named.Substring(0, equals).Trim()] = named.Substring(equals + 1); continue; } }
                    if (positional < macro.Parameters.Count) values[macro.Parameters[positional++]] = argument;
                }
                foreach (string parameter in macro.Parameters) if (!values.ContainsKey(parameter)) values[parameter] = string.Empty;
                foreach (var pair in values) body = body.Replace("{" + pair.Key + "}", pair.Value);
                return await ProcessTextAsync(body, macro.Source ?? source, includeDepth, depth).ConfigureAwait(false);
            }

            private async Task<string> IncludeAsync(string expression, string path, string symbol, WmlPreprocessorExpressionKind kind, string? source, int includeDepth, int macroDepth)
            {
                if (_resolver == null)
                {
                    if (kind == WmlPreprocessorExpressionKind.MacroInvocation) Error("WML2014", $"Unknown macro '{symbol}'; include fallback '{path}' did not run because no source resolver is configured.", source, Context(false));
                    else Error("WML2010", $"No source resolver is configured for include '{path}'.", source, Context(false));
                    return string.Empty;
                }
                WmlSource? resolved;
                try { resolved = await _resolver.ResolveAsync(path, source, _cancellation).ConfigureAwait(false); }
                catch (Exception ex) { FailedFallback($"failed: {ex.Message}"); return string.Empty; }
                if (resolved == null) { FailedFallback("was not found."); return string.Empty; }
                if (!_includes.Add(resolved.Path)) { Error("WML2013", $"Include cycle detected at '{resolved.Path}'.", source, Context(true)); return string.Empty; }
                try { return await ProcessTextAsync(resolved.Text, resolved.Path, includeDepth, macroDepth).ConfigureAwait(false); }
                finally { _includes.Remove(resolved.Path); }

                WmlPreprocessorDiagnosticContext Context(bool attempted) => new WmlPreprocessorDiagnosticContext(expression, symbol, kind, false, attempted, attempted ? path : null);
                void FailedFallback(string outcome)
                {
                    if (kind == WmlPreprocessorExpressionKind.MacroInvocation) Error("WML2014", $"Unknown macro '{symbol}'; include fallback '{path}' {outcome}", source, Context(true));
                    else Error(outcome.StartsWith("failed:", StringComparison.Ordinal) ? "WML2011" : "WML2012", $"Include '{path}' {outcome}", source, Context(true));
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

            private void Append(StringBuilder output, string value, string? source) { long bytes = Encoding.UTF8.GetByteCount(value); _bytes += bytes; if (_bytes > _options.MaxOutputBytes) throw new WmlException("Maximum preprocessor output size exceeded."); int start = output.Length; output.Append(value); _map.Add(new WmlSourceMapEntry(start, value.Length, source)); }
            private void ShiftMapEntries(int start, int offset) { for (int i = start; i < _map.Count; i++) { var entry = _map[i]; _map[i] = new WmlSourceMapEntry(entry.OutputStart + offset, entry.OutputLength, entry.Source); } }
            private void Error(string code, string message, string? source, WmlPreprocessorDiagnosticContext? context = null) => Report(code, message, WmlDiagnosticSeverity.Error, source, context);
            private void Report(string code, string message, WmlDiagnosticSeverity severity, string? source, WmlPreprocessorDiagnosticContext? context = null) => _diagnostics.Add(new WmlDiagnostic(code, message, severity, new WmlSourceSpan(source, 0, 0, 1, 1), context));
            private readonly struct Condition { public Condition(bool parentEnabled, bool branch) { ParentEnabled = parentEnabled; Branch = branch; } public bool ParentEnabled { get; } public bool Branch { get; } }
            private static List<string> ReadLines(string text) { var result = new List<string>(); int start = 0; for (int i = 0; i < text.Length; i++) if (text[i] == '\n') { result.Add(text.Substring(start, i - start + 1)); start = i + 1; } if (start < text.Length) result.Add(text.Substring(start)); return result; }
            private static int FindCall(string s, int start) { bool raw = false; for (int i = start; i < s.Length; i++) { if (!raw && i + 1 < s.Length && s[i] == '<' && s[i + 1] == '<') { raw = true; i++; } else if (raw && i + 1 < s.Length && s[i] == '>' && s[i + 1] == '>') { raw = false; i++; } else if (!raw && s[i] == '{') return i; } return -1; }
            private static int MatchingBrace(string s, int open) { int depth = 0; bool raw = false; for (int i = open; i < s.Length; i++) { if (!raw && i + 1 < s.Length && s[i] == '<' && s[i + 1] == '<') { raw = true; i++; continue; } if (raw && i + 1 < s.Length && s[i] == '>' && s[i + 1] == '>') { raw = false; i++; continue; } if (raw) continue; if (s[i] == '{') depth++; else if (s[i] == '}' && --depth == 0) return i; } return -1; }
            private static int FindDirective(string text, string directive, ref bool quote, ref bool raw) { for (int i = 0; i < text.Length; i++) { if (!quote && !raw && i + 1 < text.Length && text[i] == '<' && text[i + 1] == '<') { raw = true; i++; continue; } if (raw && i + 1 < text.Length && text[i] == '>' && text[i + 1] == '>') { raw = false; i++; continue; } if (!raw && text[i] == '"') { quote = !quote; continue; } if (!quote && !raw && i <= text.Length - directive.Length && string.CompareOrdinal(text, i, directive, 0, directive.Length) == 0 && (i + directive.Length == text.Length || char.IsWhiteSpace(text[i + directive.Length]) || text[i + directive.Length] == '#')) return i; } return -1; }
            private static bool IsSymbol(string value) { if (value.Length == 0) return false; foreach (char c in value) if (!(char.IsLetterOrDigit(c) || c == '_')) return false; return true; }
            private static List<string> Tokens(string input) { var result = new List<string>(); var b = new StringBuilder(); int paren = 0; bool quote = false; foreach (char c in input) { if (c == '"') quote = !quote; if (!quote && c == '(') paren++; if (!quote && c == ')') paren--; if (!quote && paren == 0 && char.IsWhiteSpace(c)) { if (b.Length > 0) { result.Add(b.ToString()); b.Clear(); } } else b.Append(c); } if (b.Length > 0) result.Add(b.ToString()); return result; }
            private static string ExtractOptionalArguments(string body, IDictionary<string, string> optional)
            {
                var lines = ReadLines(body); var output = new StringBuilder();
                for (int i = 0; i < lines.Count; i++)
                {
                    string trimmed = lines[i].Trim(); if (!trimmed.StartsWith("#arg ", StringComparison.Ordinal)) { output.Append(lines[i]); continue; }
                    string name = Tokens(trimmed.Substring(5)).FirstOrDefault() ?? ""; var value = new StringBuilder(); while (++i < lines.Count && !lines[i].Trim().StartsWith("#endarg", StringComparison.Ordinal)) value.Append(lines[i]); if (name.Length > 0) optional[name] = value.ToString();
                }
                return output.ToString();
            }
            private static int CompareVersions(string a, string b) { var aa = a.Split('.'); var bb = b.Split('.'); for (int i = 0; i < Math.Max(aa.Length, bb.Length); i++) { int av = i < aa.Length && int.TryParse(aa[i], out var x) ? x : 0; int bv = i < bb.Length && int.TryParse(bb[i], out var y) ? y : 0; int c = av.CompareTo(bv); if (c != 0) return c; } return 0; }
        }
    }
}
