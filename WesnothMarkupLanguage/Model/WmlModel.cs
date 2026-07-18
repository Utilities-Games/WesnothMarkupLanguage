using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

namespace WesnothMarkupLanguage
{
    public abstract class WmlNode
    {
        public WmlSourceSpan? Span { get; internal set; }
        public WmlExpansionProvenance? Provenance { get; internal set; }
        internal WmlDocument? Owner { get; set; }
        protected void Changed() { if (Owner != null) Owner.IsModified = true; }
    }

    internal sealed class WmlNodeList : IList<WmlNode>
    {
        private readonly List<WmlNode> _items = new List<WmlNode>(); private readonly Action<WmlNode> _attach; private readonly Action _changed;
        internal WmlNodeList(Action<WmlNode> attach, Action changed) { _attach = attach; _changed = changed; }
        public WmlNode this[int index] { get => _items[index]; set { _attach(value); _items[index] = value; _changed(); } }
        public int Count => _items.Count; public bool IsReadOnly => false;
        public void Add(WmlNode item) { _attach(item); _items.Add(item); _changed(); }
        internal void AddParsed(WmlNode item) { _attach(item); _items.Add(item); }
        public void Clear() { _items.Clear(); _changed(); }
        public bool Contains(WmlNode item) => _items.Contains(item); public void CopyTo(WmlNode[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public IEnumerator<WmlNode> GetEnumerator() => _items.GetEnumerator(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator(); public int IndexOf(WmlNode item) => _items.IndexOf(item);
        public void Insert(int index, WmlNode item) { _attach(item); _items.Insert(index, item); _changed(); }
        public bool Remove(WmlNode item) { bool removed = _items.Remove(item); if (removed) _changed(); return removed; }
        public void RemoveAt(int index) { _items.RemoveAt(index); _changed(); }
    }

    public sealed class WmlDocument : WmlNode
    {
        private readonly WmlNodeList _children;
        internal WmlDocument(string? originalText = null) { OriginalText = originalText; Owner = this; _children = new WmlNodeList(Attach, () => IsModified = true); }
        public WmlDocument() : this(null) { }
        internal string? OriginalText { get; }
        internal bool IsModified { get; set; }
        public IList<WmlNode> Children => _children;
        public IEnumerable<WmlTag> Tags => _children.OfType<WmlTag>();
        public IEnumerable<WmlAttribute> Attributes => _children.OfType<WmlAttribute>();
        public IEnumerable<WmlTag> FindTags(string name, bool recursive = true)
        {
            foreach (var tag in Tags)
            {
                if (tag.Name == name) yield return tag;
                if (recursive) foreach (var child in tag.FindTags(name, true)) yield return child;
            }
        }
        public void Add(WmlNode node) { _children.Add(node); }
        internal void AddParsed(WmlNode node) { _children.AddParsed(node); }
        private void Attach(WmlNode node) { node.Owner = this; if (node is WmlTag tag) tag.Attach(this); }
    }

    public sealed class WmlTag : WmlNode
    {
        private string _name;
        private readonly WmlNodeList _children;
        public WmlTag(string name) { _name = name ?? throw new ArgumentNullException(nameof(name)); _children = new WmlNodeList(AttachChild, Changed); }
        public string Name { get => _name; set { _name = value ?? throw new ArgumentNullException(nameof(value)); Changed(); } }
        public bool IsAmendment { get; set; }
        public WmlSourceSpan? ClosingSpan { get; internal set; }
        public WmlExpansionProvenance? ClosingProvenance { get; internal set; }
        public IList<WmlNode> Children => _children;
        public IEnumerable<WmlTag> Tags => _children.OfType<WmlTag>();
        public IEnumerable<WmlAttribute> Attributes => _children.OfType<WmlAttribute>();
        public string? GetAttribute(string name) => Attributes.LastOrDefault(a => a.Key == name)?.Value.Text;
        public IEnumerable<WmlTag> FindTags(string name, bool recursive = false)
        {
            foreach (var tag in Tags) { if (tag.Name == name) yield return tag; if (recursive) foreach (var child in tag.FindTags(name, true)) yield return child; }
        }
        public void Add(WmlNode node) { _children.Add(node); }
        internal void AddParsed(WmlNode node) { _children.AddParsed(node); }
        private void AttachChild(WmlNode node) { node.Owner = Owner; if (node is WmlTag tag && Owner != null) tag.Attach(Owner); }
        internal void Attach(WmlDocument owner) { Owner = owner; foreach (var c in _children) { c.Owner = owner; if (c is WmlTag t) t.Attach(owner); } }
    }

    public sealed class WmlAttribute : WmlNode
    {
        private string _key;
        private WmlValue _value;
        public WmlAttribute(string key, string value) : this(key, WmlValue.Parse(value)) { }
        public WmlAttribute(string key, WmlValue value) { _key = key; _value = value; }
        public string Key { get => _key; set { _key = value; Changed(); } }
        public WmlValue Value { get => _value; set { _value = value; Changed(); } }
    }

    public sealed class WmlComment : WmlNode { public WmlComment(string text) { Text = text; } public string Text { get; set; } }
    public sealed class WmlDirective : WmlNode { public WmlDirective(string name, string arguments) { Name = name; Arguments = arguments; } public string Name { get; set; } public string Arguments { get; set; } }
    public sealed class WmlMacroCall : WmlNode { public WmlMacroCall(string expression) { Expression = expression; } public string Expression { get; set; } }

    public enum WmlValueComponentKind { Unquoted, Quoted, Raw, Translatable, Variable, Formula }
    public sealed class WmlValueComponent
    {
        public WmlValueComponent(WmlValueComponentKind kind, string text) { Kind = kind; Text = text; }
        public WmlValueComponentKind Kind { get; }
        public string Text { get; }
    }
    public sealed class WmlValue
    {
        private readonly List<WmlValueComponent> _components;
        public WmlValue(IEnumerable<WmlValueComponent> components) { _components = new List<WmlValueComponent>(components); }
        public IReadOnlyList<WmlValueComponent> Components => _components;
        public string Text => string.Concat(_components.Select(c => c.Text));
        public static WmlValue Parse(string text) => WmlValueParser.Parse(text ?? string.Empty);
        public override string ToString() => Text;
    }
}
