namespace WesnothMarkupLanguage.Contracts
{
    /// <summary>
    /// An interface for implementing various types of Special Attribute Values such as a quoted value, variable, formula expression, etc. See <seealso href="https://wiki.wesnoth.org/SyntaxWML#Special_Attribute_Values"/>
    /// </summary>
    public interface IAttributeValue {
        /// <summary>
        /// Raw string value pulled from the config file.
        /// </summary>
        string RawValue { get; }

        /// <summary>
        /// Gets the Attribute Value as the type, <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Generic type that the <see cref="RawValue"/> should be processed and cast into.</typeparam>
        /// <returns>Processed result into type <typeparamref name="T"/>.</returns>
        T Get<T>();

        /// <summary>
        /// Sets the Attribute Value as the type, <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Generic type that should be processed into the <see cref="RawValue"/>.</typeparam>
        /// <param name="value">Processed result that should be converted into the <see cref="RawValue"/>.</param>
        void Set<T>(T value);
    }


}
