using System;

namespace WesnothMarkupLanguage
{
    public enum WmlDiagnosticSeverity { Info, Warning, Error }

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
        { Code = code; Message = message; Severity = severity; Span = span; }
        public string Code { get; }
        public string Message { get; }
        public WmlDiagnosticSeverity Severity { get; }
        public WmlSourceSpan Span { get; }
        public override string ToString() => $"{Span}: {Severity} {Code}: {Message}";
    }

    public class WmlException : Exception
    {
        public WmlException(string message, WmlSourceSpan? span = null, Exception? inner = null)
            : base(span == null ? message : $"{span}: {message}", inner) { Span = span; }
        public WmlSourceSpan? Span { get; }
    }
}
