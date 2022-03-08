using System;
using System.Collections.Generic;
using System.Text;

namespace WesnothMarkupLanguage.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class TagAttribute : Attribute
    {
        /// <summary>
        /// The name of the tag as it is used in the WML file.
        /// </summary>
        public string Name { get; }

        public TagAttribute(string name)
        {
            Name = name;
        }
    }
}
