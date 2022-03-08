using System.Collections.Generic;

namespace WesnothMarkupLanguage.Contracts
{
    /// <summary>
    /// A configuration tag. See <seealso href="https://wiki.wesnoth.org/SyntaxWML#Tag_and_Attribute_Structures"/>.
    /// </summary>
    public interface ITag {
        /// <summary>
        /// Name of the tag.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Collection of child tags.
        /// </summary>
        public List<ITag> Children { get; set; }

        /// <summary>
        /// Collection of key-value pairs of attributes.
        /// </summary>
        public List<IAttribute> Attributes { get; set; }
    }


}
