using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WesnothMarkupLanguage
{
    /// <summary>A lossless WML parse result. The original text is retained verbatim.</summary>
    public sealed class WmlSyntaxTree
    {
        internal WmlSyntaxTree(string text, string? sourceName, WmlDocument document, IReadOnlyList<WmlDiagnostic> diagnostics)
        { Text = text; SourceName = sourceName; Document = document; Diagnostics = diagnostics; }
        public string Text { get; }
        public string? SourceName { get; }
        public WmlDocument Document { get; }
        public IReadOnlyList<WmlDiagnostic> Diagnostics { get; }
        public bool HasErrors { get { foreach (var d in Diagnostics) if (d.Severity == WmlDiagnosticSeverity.Error) return true; return false; } }
        public override string ToString() => Text;
    }

    public static class WmlParser
    {
        public static WmlSyntaxTree Parse(string text, string? sourceName = null) => WmlParsing.Parse(text ?? string.Empty, sourceName);
        public static WmlSyntaxTree Parse(TextReader reader, string? sourceName = null) => Parse(reader.ReadToEnd(), sourceName);
        public static WmlSyntaxTree Parse(Stream stream, Encoding? encoding = null, string? sourceName = null)
        {
            using (var reader = new StreamReader(stream, encoding ?? new UTF8Encoding(false), true, 4096, true))
                return Parse(reader, sourceName);
        }
    }
}
