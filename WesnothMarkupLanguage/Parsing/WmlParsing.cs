using System;
using System.Collections.Generic;
using System.Text;

namespace WesnothMarkupLanguage
{
    internal static class WmlParsing
    {
        internal static WmlSyntaxTree Parse(string text, string? source)
        {
            var diagnostics = new List<WmlDiagnostic>();
            var document = new WmlDocument(text);
            var stack = new Stack<WmlTag>();
            var lines = SplitLines(text);
            for (int i = 0; i < lines.Count; i++)
            {
                var item = lines[i];
                string logical = item.Text;
                while (NeedsContinuation(logical) && i + 1 < lines.Count) logical += lines[++i].Text;
                ParseStatements(logical, item.Start, item.LineNumber, source, document, stack, diagnostics);
            }
            while (stack.Count > 0)
            {
                var tag = stack.Pop();
                diagnostics.Add(Diagnostic("WML1003", $"Tag [{tag.Name}] is not closed.", source, tag.Span?.Start ?? text.Length, 0, tag.Span?.Line ?? 1, tag.Span?.Column ?? 1));
            }
            document.IsModified = false;
            return new WmlSyntaxTree(text, source, document, diagnostics);
        }

        private static void ParseStatements(string raw, int start, int line, string? source, WmlDocument doc, Stack<WmlTag> stack, List<WmlDiagnostic> diagnostics)
        {
            int position = 0;
            while (position < raw.Length)
            {
                while (position < raw.Length && char.IsWhiteSpace(raw[position])) position++;
                if (position >= raw.Length) return;
                int statementLine = line, lineStart = 0;
                for (int i = 0; i < position; i++) if (raw[i] == '\n') { statementLine++; lineStart = i + 1; }
                if (raw[position] == '[')
                {
                    int close = raw.IndexOf(']', position); if (close < 0) { ParseStatement(raw.Substring(position), start + position, statementLine, position - lineStart, source, doc, stack, diagnostics); return; }
                    ParseStatement(raw.Substring(position, close - position + 1), start + position, statementLine, position - lineStart, source, doc, stack, diagnostics); position = close + 1; continue;
                }
                int nextTag = FindNextTagStart(raw, position + 1);
                if (nextTag >= 0)
                {
                    ParseStatement(raw.Substring(position, nextTag - position), start + position, statementLine, position - lineStart, source, doc, stack, diagnostics); position = nextTag; continue;
                }
                ParseStatement(raw.Substring(position), start + position, statementLine, position - lineStart, source, doc, stack, diagnostics); return;
            }
        }

        private static void ParseStatement(string raw, int start, int line, int columnOffset, string? source, WmlDocument doc, Stack<WmlTag> stack, List<WmlDiagnostic> diagnostics)
        {
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) return;
            int localStart = raw.IndexOf(trimmed, StringComparison.Ordinal), column = columnOffset + localStart + 1;
            var span = new WmlSourceSpan(source, start + localStart, trimmed.Length, line, column);
            if (trimmed[0] == '#')
            {
                if (IsDirective(trimmed, out var name, out var args)) Add(new WmlDirective(name, args) { Span = span }, doc, stack);
                else Add(new WmlComment(trimmed.Substring(1)) { Span = span }, doc, stack);
                return;
            }
            if (trimmed[0] == '[')
            {
                int close = trimmed.IndexOf(']');
                if (close < 0) { diagnostics.Add(Diagnostic("WML1001", "Unterminated tag header.", source, span.Start, span.Length, line, column)); return; }
                string name = trimmed.Substring(1, close - 1).Trim();
                if (name.StartsWith("/", StringComparison.Ordinal))
                {
                    string closing = name.Substring(1);
                    if (stack.Count == 0) { diagnostics.Add(Diagnostic("WML1002", $"Unexpected closing tag [/{closing}].", source, span.Start, span.Length, line, column)); return; }
                    var opened = stack.Pop();
                    opened.ClosingSpan = span;
                    if (!string.Equals(opened.Name, closing, StringComparison.Ordinal)) diagnostics.Add(Diagnostic("WML1004", $"Closing tag [/{closing}] does not match [{opened.Name}].", source, span.Start, span.Length, line, column));
                    if (opened.IsAmendment) ApplyAmendment(opened, doc, stack);
                    return;
                }
                bool amendment = name.StartsWith("+", StringComparison.Ordinal);
                if (amendment) name = name.Substring(1);
                if (!IsName(name)) { diagnostics.Add(Diagnostic("WML1005", $"Invalid tag name '{name}'.", source, span.Start, span.Length, line, column)); return; }
                var tag = new WmlTag(name) { Span = span, IsAmendment = amendment };
                if (!amendment) Add(tag, doc, stack);
                stack.Push(tag);
                return;
            }
            if (trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}') { Add(new WmlMacroCall(trimmed.Substring(1, trimmed.Length - 2)) { Span = span }, doc, stack); return; }
            int equals = FindOutside(trimmed, '=');
            if (equals >= 0)
            {
                string keysText = trimmed.Substring(0, equals).Trim();
                string valueText = StripComments(trimmed.Substring(equals + 1)).Trim();
                var keys = SplitOutside(keysText, ','); var values = SplitOutside(valueText, ',');
                for (int k = 0; k < keys.Count; k++)
                {
                    string key = keys[k].Trim();
                    if (!IsName(key)) { diagnostics.Add(Diagnostic("WML1006", $"Invalid attribute name '{key}'.", source, span.Start, span.Length, line, column)); continue; }
                    string value = k < values.Count ? values[k].Trim() : string.Empty;
                    if (k == keys.Count - 1 && values.Count > keys.Count) value = string.Join(",", values.GetRange(k, values.Count - k)).Trim();
                    Add(new WmlAttribute(key, WmlValue.Parse(value)) { Span = span }, doc, stack);
                }
                return;
            }
            diagnostics.Add(Diagnostic("WML1007", "Unrecognized WML statement.", source, span.Start, span.Length, line, column));
        }

        private static void ApplyAmendment(WmlTag amendment, WmlDocument doc, Stack<WmlTag> stack)
        {
            IList<WmlNode> siblings = stack.Count == 0 ? doc.Children : stack.Peek().Children;
            WmlTag? target = null;
            for (int i = siblings.Count - 1; i >= 0; i--) if (siblings[i] is WmlTag t && t.Name == amendment.Name) { target = t; break; }
            if (target == null) { amendment.IsAmendment = false; Add(amendment, doc, stack); return; }
            foreach (var child in amendment.Children)
            {
                if (child is WmlAttribute a)
                {
                    for (int i = target.Children.Count - 1; i >= 0; i--) if (target.Children[i] is WmlAttribute old && old.Key == a.Key) { target.Children.RemoveAt(i); break; }
                }
                target.AddParsed(child);
            }
        }

        private static void Add(WmlNode node, WmlDocument doc, Stack<WmlTag> stack) { if (stack.Count == 0) doc.AddParsed(node); else stack.Peek().AddParsed(node); }
        private static bool IsDirective(string s, out string name, out string args)
        {
            int p = 1; while (p < s.Length && char.IsLetter(s[p])) p++;
            name = s.Substring(1, p - 1); args = s.Substring(p).Trim();
            switch (name) { case "define": case "enddef": case "arg": case "endarg": case "undef": case "ifdef": case "ifndef": case "ifhave": case "ifnhave": case "ifver": case "ifnver": case "else": case "endif": case "error": case "warning": case "deprecated": case "textdomain": return true; default: return false; }
        }
        private static bool IsName(string s) { if (s.Length == 0) return false; foreach (char c in s) if (!(char.IsLetterOrDigit(c) || c == '_')) return false; return true; }
        private static WmlDiagnostic Diagnostic(string code, string message, string? source, int start, int length, int line, int col) => new WmlDiagnostic(code, message, WmlDiagnosticSeverity.Error, new WmlSourceSpan(source, start, length, line, col));

        private sealed class Line { public string Text = ""; public int Start; public int LineNumber; }
        private static List<Line> SplitLines(string text)
        {
            var result = new List<Line>(); int start = 0, number = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') { result.Add(new Line { Text = text.Substring(start, i - start + 1), Start = start, LineNumber = number++ }); start = i + 1; }
            if (start < text.Length || text.Length == 0) result.Add(new Line { Text = text.Substring(start), Start = start, LineNumber = number });
            return result;
        }
        private static bool NeedsContinuation(string text)
        {
            text = StripComments(text);
            bool quote = false, raw = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (!quote && i + 1 < text.Length && text[i] == '<' && text[i + 1] == '<') { raw = true; i++; continue; }
                if (raw && i + 1 < text.Length && text[i] == '>' && text[i + 1] == '>') { raw = false; i++; continue; }
                if (!raw && text[i] == '"') { if (quote && i + 1 < text.Length && text[i + 1] == '"') i++; else quote = !quote; }
            }
            return quote || raw || text.TrimEnd().EndsWith("+", StringComparison.Ordinal);
        }
        internal static int FindOutside(string text, char wanted)
        {
            bool quote = false, raw = false; int parentheses = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (!quote && i + 1 < text.Length && text[i] == '<' && text[i + 1] == '<') { raw = true; i++; continue; }
                if (raw && i + 1 < text.Length && text[i] == '>' && text[i + 1] == '>') { raw = false; i++; continue; }
                if (!raw && text[i] == '"') { if (quote && i + 1 < text.Length && text[i + 1] == '"') i++; else quote = !quote; continue; }
                if (!quote && !raw && text[i] == '(') { parentheses++; continue; }
                if (!quote && !raw && text[i] == ')' && parentheses > 0) { parentheses--; continue; }
                if (!quote && !raw && parentheses == 0 && text[i] == wanted) return i;
            }
            return -1;
        }
        internal static List<string> SplitOutside(string text, char separator)
        {
            var result = new List<string>(); int start = 0, offset = 0;
            while (offset <= text.Length) { int found = FindOutside(text.Substring(offset), separator); if (found < 0) break; found += offset; result.Add(text.Substring(start, found - start)); start = found + 1; offset = start; }
            result.Add(text.Substring(start)); return result;
        }
        private static int FindNextTagStart(string text, int start)
        {
            bool quote = false, raw = false;
            for (int i = start; i < text.Length; i++)
            {
                if (!quote && !raw && i + 1 < text.Length && text[i] == '<' && text[i + 1] == '<') { raw = true; i++; continue; }
                if (raw && i + 1 < text.Length && text[i] == '>' && text[i + 1] == '>') { raw = false; i++; continue; }
                if (!raw && text[i] == '"') { if (quote && i + 1 < text.Length && text[i + 1] == '"') i++; else quote = !quote; continue; }
                if (!quote && !raw && text[i] == '[' && IsTagStart(text, i) && (i == 0 || char.IsWhiteSpace(text[i - 1]))) return i;
            }
            return -1;
        }
        private static bool IsTagStart(string text, int start)
        {
            int i = start + 1; if (i < text.Length && (text[i] == '/' || text[i] == '+')) i++;
            int nameStart = i; while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
            if (i == nameStart) return false; while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            return i < text.Length && text[i] == ']';
        }
        private static string StripComments(string value)
        {
            var result = new StringBuilder(); bool quote = false, raw = false; int parentheses = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (!quote && !raw && i + 1 < value.Length && value[i] == '<' && value[i + 1] == '<') { raw = true; result.Append(value[i]); result.Append(value[++i]); continue; }
                if (raw && i + 1 < value.Length && value[i] == '>' && value[i + 1] == '>') { raw = false; result.Append(value[i]); result.Append(value[++i]); continue; }
                if (!raw && value[i] == '"') { result.Append(value[i]); if (quote && i + 1 < value.Length && value[i + 1] == '"') result.Append(value[++i]); else quote = !quote; continue; }
                if (!quote && !raw && value[i] == '(') parentheses++;
                else if (!quote && !raw && value[i] == ')' && parentheses > 0) parentheses--;
                if (!quote && !raw && parentheses == 0 && value[i] == '#') { while (i + 1 < value.Length && value[i + 1] != '\n') i++; continue; }
                result.Append(value[i]);
            }
            return result.ToString();
        }
    }

    internal static class WmlValueParser
    {
        internal static WmlValue Parse(string value)
        {
            var parts = WmlParsing.SplitOutside(value, '+'); var components = new List<WmlValueComponent>();
            foreach (var part0 in parts)
            {
                string part = part0.Trim(); bool translatable = false;
                if (part.StartsWith("_", StringComparison.Ordinal)) { translatable = true; part = part.Substring(1).TrimStart(); }
                WmlValueComponentKind kind;
                string text;
                if (part.StartsWith("<<", StringComparison.Ordinal) && part.EndsWith(">>", StringComparison.Ordinal) && part.Length >= 4) { kind = translatable ? WmlValueComponentKind.Translatable : WmlValueComponentKind.Raw; text = part.Substring(2, part.Length - 4); }
                else if (part.StartsWith("\"", StringComparison.Ordinal) && part.EndsWith("\"", StringComparison.Ordinal) && part.Length >= 2) { kind = translatable ? WmlValueComponentKind.Translatable : WmlValueComponentKind.Quoted; text = part.Substring(1, part.Length - 2).Replace("\"\"", "\""); }
                else if (part.StartsWith("$(", StringComparison.Ordinal) && part.EndsWith(")", StringComparison.Ordinal)) { kind = WmlValueComponentKind.Formula; text = part; }
                else if (part.StartsWith("$", StringComparison.Ordinal)) { kind = WmlValueComponentKind.Variable; text = part; }
                else { kind = translatable ? WmlValueComponentKind.Translatable : WmlValueComponentKind.Unquoted; text = part; }
                components.Add(new WmlValueComponent(kind, text));
            }
            return new WmlValue(components);
        }
    }
}
