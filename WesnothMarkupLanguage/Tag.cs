using System.Collections.Generic;
using System.Text;
using WesnothMarkupLanguage.Contracts;

namespace WesnothMarkupLanguage
{
    public class Tag : ITag
    {
        public string Name { get; set; } = "undefined";

        public List<ITag> Children {get;set; } = new List<ITag>();

        public List<IAttribute> Attributes {get;set;} = new List<IAttribute>();

        public Tag(string name)
        {
            Name = name;
        }
    }
}
