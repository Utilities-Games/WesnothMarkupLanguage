using System;

namespace WesnothMarkupLanguage
{
    public enum WmlDiagnosticSeverity { Info, Warning, Error }

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
        { Code = code; Message = message; Severity = severity; Span = span; PreprocessorContext = preprocessorContext; }
        public string Code { get; }
        public string Message { get; }
        public WmlDiagnosticSeverity Severity { get; }
        public WmlSourceSpan Span { get; }
        public WmlPreprocessorDiagnosticContext? PreprocessorContext { get; }
        public override string ToString() => $"{Span}: {Severity} {Code}: {Message}";
    }

    public class WmlException : Exception
    {
        public WmlException(string message, WmlSourceSpan? span = null, Exception? inner = null)
            : base(span == null ? message : $"{span}: {message}", inner) { Span = span; }
        public WmlSourceSpan? Span { get; }
    }
}
