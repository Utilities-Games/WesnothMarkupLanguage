using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WesnothMarkupLanguage.Contracts;

namespace WesnothMarkupLanguage.Serialization
{
    public static class ConfigReader
    {
        public static IConfig Read(StreamReader sr)
        {
            IConfig config = new Config();
            Stack<ITag> TagStack = new Stack<ITag>();
            ITag? currentTag = null;
            IAttribute? currentAttribute = null;

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();
                string trimmedLine = line.Trim();

                if (string.IsNullOrEmpty(trimmedLine)) continue;

                if (trimmedLine[0] == '[')
                {
                    string tagName = line.Remove(line.IndexOf(']')).Remove(0, line.IndexOf('[') + 1);
                    if (tagName.StartsWith('/'))
                    {
                        ITag? completedTag = null;
                        if (TagStack.TryPop(out completedTag))
                        {
                            if (!TagStack.TryPeek(out currentTag))
                            {
                                config.TopLevelTags.Add(completedTag);
                            }
                            else
                            {
                                currentTag.Children.Add(completedTag);
                            }
                        }
                    }
                    else
                    {
                        currentTag = new Tag(tagName);
                        TagStack.Push(currentTag);
                    }
                }
                else if (trimmedLine[0] == '#')
                {

                }
                else if (trimmedLine[0] == '{')
                {

                }
                else if (trimmedLine.Contains('='))
                {
                    string attributeKey = trimmedLine.Remove(trimmedLine.IndexOf('='));
                    string attributeValue = trimmedLine.Remove(0, trimmedLine.IndexOf('=') + 1);

                    currentAttribute = new SimpleAttribute(attributeKey, attributeValue);

                    // Check for odd count of double-quotes. If odd, thenthis is a multi-line text.
                    int doubleQuoteCount = currentAttribute.Value.ToCharArray().Count(c => c == '"');
                    while (doubleQuoteCount % 2 != 0)
                    {
                        currentAttribute.Value += sr.ReadLine();
                        doubleQuoteCount = currentAttribute.Value.ToCharArray().Count(c => c == '"');
                    }

                    if (currentTag != null)
                    {
                        currentTag.Attributes.Add(currentAttribute);
                    }
                }
                else
                {

                }
            }

            return config;
        }

        public static IEnumerable<ITag> Find(this IConfig config, string tagName)
        {
            List<ITag> tags = new List<ITag>();

            for (int i = 0; i < config.TopLevelTags.Count; i++)
            {
                var recursiveFind = config.TopLevelTags[i].Find(tagName);
                if (recursiveFind.Any())
                {
                    tags.AddRange(recursiveFind);
                }
            }

            return tags;
        }
        private static IEnumerable<ITag> Find(this ITag tag, string tagName)
        {
            var tags = new List<ITag>();

            if (tag.Name == tagName) tags.Add(tag);

            for (int i = 0; i < tag.Children.Count; i++)
            {
                if (tag.Children[i].Name == tagName) tags.Add(tag.Children[i]);
                var recursiveFind = tag.Children[i].Find(tagName);
                if (recursiveFind.Any())
                {
                    tags.AddRange(recursiveFind);
                }
            }
            return tags;
        }

        public static object ToType(this ITag tag)
        {
            Type tagType = Type.GetType($"WesnothMarkupLanguage.Tags.{tag.Name}");
            if (tagType == null)
            {
                // Try to search for it by the TagAttribute
            }

            if (tagType == null) throw new MissingMemberException("Could not find Type for Tag named '" + tag.Name + "'.");

            var obj = tagType.GetConstructor(new Type[] { }).Invoke(new object[] { });

            var properties = tagType.GetProperties();
            foreach (var attribute in tag.Attributes)
            {
                var attributeProperty = properties.FirstOrDefault(o => o.Name == attribute.Key);
                if (attributeProperty != null)
                {
                    object? value = null;
                    try
                    {
                        value = Convert.ChangeType(attribute.Value, attributeProperty.PropertyType);
                    }
                    catch (Exception)
                    {
                        // Probably do something
                    } finally
                    {
                        attributeProperty.SetValue(obj, value);
                    }
                }
            }

            return obj;
        }
    }
}
