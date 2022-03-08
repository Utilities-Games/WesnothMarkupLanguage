namespace WesnothMarkupLanguage.Contracts
{
    /// <summary>
    /// A key-value pair contained within a tag or configuration structure. See <seealso href="https://wiki.wesnoth.org/SyntaxWML#Tag_and_Attribute_Structures"/>
    /// </summary>
    public interface IAttribute
    {
        /// <summary>
        /// Name of the attribute.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Interpreted value of the attribute.
        /// </summary>
        public string Value { get; set; }
    }
}
