using System;
using WesnothMarkupLanguage.Contracts;

namespace WesnothMarkupLanguage
{
    public class SimpleAttributeValue : IAttributeValue
    {
        public string _rawValue { get; set; }
        public string RawValue => _rawValue;

        public SimpleAttributeValue(string value)
        {
            _rawValue = value;
        }

        public T Get<T>()
        {
            return (T)Convert.ChangeType(RawValue, typeof(T));
        }

        public void Set<T>(T value)
        {
            _rawValue = value.ToString();
        }
    }

}
