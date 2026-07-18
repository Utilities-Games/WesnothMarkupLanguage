using System;

namespace WesnothMarkupLanguage
{
    public enum WmlDiagnosticSeverity { Info, Warning, Error }

    public enum WmlSourceReferencePrecision { Unknown, FileOnly, Exact }

    public sealed class WmlSourceReference
    {
        public WmlSourceReference(string? source, WmlSourceReferencePrecision precision, int start, int length, int line, int column)
        {
            Source = source;
            Precision = precision;
            Start = start;
            Length = length;
            Line = line;
            Column = column;
        }

        public WmlSourceReference(WmlSourceSpan span)
            : this(span.Source, WmlSourceReferencePrecision.Exact, span.Start, span.Length, span.Line, span.Column) { }

        public string? Source { get; }
        public WmlSourceReferencePrecision Precision { get; }
        public int Start { get; }
        public int Length { get; }
        public int Line { get; }
        public int Column { get; }
        public WmlSourceSpan? Span => Precision == WmlSourceReferencePrecision.Exact ? new WmlSourceSpan(Source, Start, Length, Line, Column) : null;

        public static WmlSourceReference Unknown { get; } = new WmlSourceReference(null, WmlSourceReferencePrecision.Unknown, 0, 0, 1, 1);
        public static WmlSourceReference FileOnly(string? source) => new WmlSourceReference(source, source == null ? WmlSourceReferencePrecision.Unknown : WmlSourceReferencePrecision.FileOnly, 0, 0, 1, 1);
        public static WmlSourceReference Exact(WmlSourceSpan span) => new WmlSourceReference(span);
        public override string ToString() => Precision == WmlSourceReferencePrecision.Exact ? $"{Source ?? "<input>"}({Line},{Column})" : Source ?? "<unknown>";
    }

    public sealed class WmlMacroExpansionFrame
    {
        public WmlMacroExpansionFrame(string macroSymbol, WmlSourceReference? definition, WmlSourceReference? invocation)
        {
            MacroSymbol = macroSymbol;
            Definition = definition ?? WmlSourceReference.Unknown;
            Invocation = invocation ?? WmlSourceReference.Unknown;
        }

        public string MacroSymbol { get; }
        public WmlSourceReference Definition { get; }
        public WmlSourceReference Invocation { get; }
        public WmlSourceSpan? DefinitionSpan => Definition.Span;
        public WmlSourceSpan? InvocationSpan => Invocation.Span;
    }

    public sealed class WmlExpansionProvenance
    {
        public WmlExpansionProvenance(WmlSourceReference? source, System.Collections.Generic.IEnumerable<WmlMacroExpansionFrame>? expansionChain = null)
        {
            Source = source ?? WmlSourceReference.Unknown;
            ExpansionChain = new System.Collections.Generic.List<WmlMacroExpansionFrame>(expansionChain ?? Array.Empty<WmlMacroExpansionFrame>());
        }

        public WmlSourceReference Source { get; }
        public string? LogicalSource => Source.Source;
        public WmlSourceSpan? SourceSpan => Source.Span;
        public System.Collections.Generic.IReadOnlyList<WmlMacroExpansionFrame> ExpansionChain { get; }
    }

    public enum WmlPreprocessorExpressionKind { Include, MacroInvocation, Ambiguous }

    public sealed class WmlPreprocessorDiagnosticContext
    {
        public WmlPreprocessorDiagnosticContext(string expression, string? symbol, WmlPreprocessorExpressionKind expressionKind, bool macroWasRegistered, bool includeFallbackAttempted, string? includeCandidate)
        {
            Expression = expression;
            Symbol = symbol;
            ExpressionKind = expressionKind;
            MacroWasRegistered = macroWasRegistered;
            IncludeFallbackAttempted = includeFallbackAttempted;
            IncludeCandidate = includeCandidate;
        }

        public string Expression { get; }
        public string? Symbol { get; }
        public WmlPreprocessorExpressionKind ExpressionKind { get; }
        public bool MacroWasRegistered { get; }
        public bool IncludeFallbackAttempted { get; }
        public string? IncludeCandidate { get; }
    }

    public sealed class WmlSourceSpan
    {
        public WmlSourceSpan(string? source, int start, int length, int line, int column)
        { Source = source; Start = start; Length = length; Line = line; Column = column; }
        public string? Source { get; }
        public int Start { get; }
        public int Length { get; }
        public int Line { get; }
        public int Column { get; }
        public override string ToString() => $"{Source ?? "<input>"}({Line},{Column})";
    }

    public sealed class WmlDiagnostic
    {
        public WmlDiagnostic(string code, string message, WmlDiagnosticSeverity severity, WmlSourceSpan span)
            : this(code, message, severity, span, null) { }
        public WmlDiagnostic(string code, string message, WmlDiagnosticSeverity severity, WmlSourceSpan span, WmlPreprocessorDiagnosticContext? preprocessorContext)
            : this(code, message, severity, span, preprocessorContext, null) { }
        public WmlDiagnostic(string code, string message, WmlDiagnosticSeverity severity, WmlSourceSpan span, WmlPreprocessorDiagnosticContext? preprocessorContext, WmlExpansionProvenance? provenance)
        { Code = code; Message = message; Severity = severity; Span = span; PreprocessorContext = preprocessorContext; Provenance = provenance; }
        public string Code { get; }
        public string Message { get; }
        public WmlDiagnosticSeverity Severity { get; }
        public WmlSourceSpan Span { get; }
        public WmlPreprocessorDiagnosticContext? PreprocessorContext { get; }
        public WmlExpansionProvenance? Provenance { get; }
        public override string ToString() => $"{Span}: {Severity} {Code}: {Message}";
    }

    public class WmlException : Exception
    {
        public WmlException(string message, WmlSourceSpan? span = null, Exception? inner = null)
            : base(span == null ? message : $"{span}: {message}", inner) { Span = span; }
        public WmlSourceSpan? Span { get; }
    }
}
