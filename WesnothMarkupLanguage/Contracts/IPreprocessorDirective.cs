namespace WesnothMarkupLanguage.Contracts
{
    /// <summary>
    /// A configuration block that creates and uses macros. See <seealso href="https://wiki.wesnoth.org/PreprocessorRef#Preprocessor_directives"/>.
    /// </summary>
    public interface IPreprocessorDirective {
        /// <summary>
        /// Name of the directive
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Arguments provided with the directive
        /// </summary>
        public string Arguments { get; set; }

        /// <summary>
        /// The body of configuration for this preprocessing directive.
        /// </summary>
        public IDirectiveBody? Body { get; set; }
    }


}
