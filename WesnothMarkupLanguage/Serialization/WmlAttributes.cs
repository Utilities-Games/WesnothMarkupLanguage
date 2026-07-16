using System;

namespace WesnothMarkupLanguage
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class WmlTagAttribute : Attribute { public WmlTagAttribute(string name) { Name = name; } public string Name { get; } }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class WmlAttributeAttribute : Attribute { public WmlAttributeAttribute(string? name = null) { Name = name; } public string? Name { get; } }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class WmlChildAttribute : Attribute { public WmlChildAttribute(string? name = null) { Name = name; } public string? Name { get; } }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class WmlExtensionDataAttribute : Attribute { }
}
