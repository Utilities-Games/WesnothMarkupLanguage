using WesnothMarkupLanguage.Contracts;

namespace WesnothMarkupLanguage
{
    public class SimpleAttribute : IAttribute
    {
        public string Key { get; set; }

        public string Value { get; set; }

        public SimpleAttribute(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}
