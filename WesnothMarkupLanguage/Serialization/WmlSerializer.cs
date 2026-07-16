using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace WesnothMarkupLanguage
{
    public static class WmlSerializer
    {
        public static WmlDocument Serialize<T>(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var document = new WmlDocument(); document.Add(SerializeTag(value, typeof(T), TagName(typeof(T)))); return document;
        }

        public static T Deserialize<T>(WmlDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var name = TagName(typeof(T)); var tag = document.Tags.FirstOrDefault(t => t.Name == name);
            if (tag == null) throw new WmlException($"Required root tag [{name}] was not found.");
            return (T)DeserializeTag(typeof(T), tag);
        }
        public static T Deserialize<T>(WmlTag tag) => (T)DeserializeTag(typeof(T), tag);

        private static WmlTag SerializeTag(object value, Type type, string name)
        {
            var tag = new WmlTag(name);
            foreach (var member in Members(type))
            {
                object? memberValue = Get(member, value); if (memberValue == null) continue;
                var attr = member.GetCustomAttribute<WmlAttributeAttribute>(); var child = member.GetCustomAttribute<WmlChildAttribute>();
                if (attr != null) tag.Add(new WmlAttribute(attr.Name ?? member.Name, ConvertToString(memberValue)));
                else if (child != null)
                {
                    Type memberType = MemberType(member); string childName = child.Name ?? TagName(ItemType(memberType));
                    if (IsCollection(memberType)) foreach (var item in (IEnumerable)memberValue) if (item != null) tag.Add(SerializeTag(item, item.GetType(), childName));
                    else tag.Add(SerializeTag(memberValue, memberType, childName));
                }
                else if (member.GetCustomAttribute<WmlExtensionDataAttribute>() != null && memberValue is IDictionary<string, string> extras)
                    foreach (var pair in extras) tag.Add(new WmlAttribute(pair.Key, pair.Value));
            }
            return tag;
        }

        private static object DeserializeTag(Type type, WmlTag tag)
        {
            object instance;
            try { instance = Activator.CreateInstance(type) ?? throw new InvalidOperationException(); }
            catch (Exception ex) { throw new WmlException($"Type {type.FullName} must have a public parameterless constructor.", tag.Span, ex); }
            var consumedAttributes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in Members(type))
            {
                var attr = member.GetCustomAttribute<WmlAttributeAttribute>(); var child = member.GetCustomAttribute<WmlChildAttribute>();
                if (attr != null)
                {
                    string key = attr.Name ?? member.Name; var source = tag.Attributes.LastOrDefault(a => a.Key == key); if (source == null) continue;
                    try { Set(member, instance, ConvertFromString(source.Value.Text, MemberType(member))); consumedAttributes.Add(key); }
                    catch (Exception ex) { throw new WmlException($"Cannot convert attribute '{key}' value '{source.Value.Text}' to {MemberType(member).Name}.", source.Span, ex); }
                }
                else if (child != null)
                {
                    Type memberType = MemberType(member); Type itemType = ItemType(memberType); string name = child.Name ?? TagName(itemType); var children = tag.Tags.Where(t => t.Name == name).ToList();
                    if (IsCollection(memberType))
                    {
                        var listType = typeof(List<>).MakeGenericType(itemType); var list = (IList)Activator.CreateInstance(listType)!; foreach (var c in children) list.Add(DeserializeTag(itemType, c)); SetCollection(member, instance, memberType, itemType, list);
                    }
                    else if (children.Count > 0) Set(member, instance, DeserializeTag(memberType, children[0]));
                }
            }
            var extension = Members(type).FirstOrDefault(m => m.GetCustomAttribute<WmlExtensionDataAttribute>() != null);
            if (extension != null)
            {
                var extras = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var a in tag.Attributes) if (!consumedAttributes.Contains(a.Key)) extras[a.Key] = a.Value.Text; Set(extension, instance, extras);
            }
            return instance;
        }

        private static object? ConvertFromString(string value, Type target)
        {
            Type actual = Nullable.GetUnderlyingType(target) ?? target;
            if (actual == typeof(string)) return value;
            if (actual == typeof(bool)) { if (value == "yes" || value == "true" || value == "1") return true; if (value == "no" || value == "false" || value == "0") return false; throw new FormatException("Expected yes/no, true/false, or 1/0."); }
            if (actual.IsEnum) return Enum.Parse(actual, value, false);
            return Convert.ChangeType(value, actual, CultureInfo.InvariantCulture);
        }
        private static string ConvertToString(object value) { if (value is bool b) return b ? "yes" : "no"; if (value is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture); return value.ToString() ?? string.Empty; }
        private static string TagName(Type type) => type.GetCustomAttribute<WmlTagAttribute>()?.Name ?? type.Name;
        private static IEnumerable<MemberInfo> Members(Type type) => type.GetMembers(BindingFlags.Instance | BindingFlags.Public).Where(m => (m is PropertyInfo p && p.CanRead && p.CanWrite) || m is FieldInfo);
        private static Type MemberType(MemberInfo member) => member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;
        private static object? Get(MemberInfo member, object target) => member is PropertyInfo p ? p.GetValue(target) : ((FieldInfo)member).GetValue(target);
        private static void Set(MemberInfo member, object target, object? value) { if (member is PropertyInfo p) p.SetValue(target, value); else ((FieldInfo)member).SetValue(target, value); }
        private static bool IsCollection(Type type) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
        private static Type ItemType(Type type) { if (type.IsArray) return type.GetElementType()!; return type.IsGenericType ? type.GetGenericArguments()[0] : type; }
        private static void SetCollection(MemberInfo member, object target, Type collectionType, Type itemType, IList list)
        {
            if (collectionType.IsArray) { var array = Array.CreateInstance(itemType, list.Count); list.CopyTo(array, 0); Set(member, target, array); }
            else if (collectionType.IsAssignableFrom(list.GetType())) Set(member, target, list);
            else { var collection = Activator.CreateInstance(collectionType); var add = collectionType.GetMethod("Add", new[] { itemType }); if (collection == null || add == null) throw new InvalidOperationException($"Collection {collectionType.Name} cannot be populated."); foreach (var item in list) add.Invoke(collection, new[] { item }); Set(member, target, collection); }
        }
    }
}
