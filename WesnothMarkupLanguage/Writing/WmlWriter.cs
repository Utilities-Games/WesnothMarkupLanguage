using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WesnothMarkupLanguage
{
    public enum WmlWriteMode { Lossless, Canonical }

    public static class WmlWriter
    {
        public static string Write(WmlSyntaxTree tree, WmlWriteMode mode = WmlWriteMode.Lossless) => mode == WmlWriteMode.Lossless && !tree.Document.IsModified ? tree.Text : Write(tree.Document, WmlWriteMode.Canonical);
        public static string Write(WmlDocument document, WmlWriteMode mode = WmlWriteMode.Canonical)
        {
            if (mode == WmlWriteMode.Lossless && !document.IsModified && document.OriginalText != null) return document.OriginalText;
            var builder = new StringBuilder(); foreach (var node in document.Children) WriteNode(builder, node, 0); return builder.ToString();
        }
        public static void Write(TextWriter writer, WmlDocument document, WmlWriteMode mode = WmlWriteMode.Canonical) => writer.Write(Write(document, mode));
        private static void WriteNode(StringBuilder b, WmlNode node, int depth)
        {
            string indent = new string(' ', depth * 4);
            if (node is WmlTag tag)
            {
                b.Append(indent).Append('[').Append(tag.Name).AppendLine("]"); foreach (var child in tag.Children) WriteNode(b, child, depth + 1); b.Append(indent).Append("[/").Append(tag.Name).AppendLine("]");
            }
            else if (node is WmlAttribute attribute) b.Append(indent).Append(attribute.Key).Append('=').Append(Format(attribute.Value)).AppendLine();
            else if (node is WmlComment comment) b.Append(indent).Append('#').Append(comment.Text).AppendLine();
            else if (node is WmlDirective directive) b.Append(indent).Append('#').Append(directive.Name).Append(directive.Arguments.Length == 0 ? "" : " " + directive.Arguments).AppendLine();
            else if (node is WmlMacroCall macro) b.Append(indent).Append('{').Append(macro.Expression).AppendLine("}");
        }
        private static string Format(WmlValue value)
        {
            var b = new StringBuilder();
            for (int i = 0; i < value.Components.Count; i++)
            {
                if (i > 0) b.Append(" + "); var c = value.Components[i];
                switch (c.Kind)
                {
                    case WmlValueComponentKind.Raw: b.Append("<<").Append(c.Text).Append(">>"); break;
                    case WmlValueComponentKind.Translatable: b.Append("_\"").Append(c.Text.Replace("\"", "\"\"")).Append('"'); break;
                    case WmlValueComponentKind.Quoted: b.Append('"').Append(c.Text.Replace("\"", "\"\"")).Append('"'); break;
                    default: b.Append(c.Text); break;
                }
            }
            return b.ToString();
        }
    }
}
