using System;
using System.Collections.Generic;
using System.Text;

namespace WesnothMarkupLanguage.Contracts
{
    /// <summary>
    /// A container for WML configuration. See <seealso href="https://wiki.wesnoth.org/SyntaxWML#Tutorial"/>.
    /// </summary>
    public interface IConfig
    {
        /// <summary>
        /// Collection of preprocessing directives defined within the configuration.
        /// </summary>
        public List<IPreprocessorDirective> Directives { get; set; }

        /// <summary>
        /// Collection of top-level tags.
        /// </summary>
        public List<ITag> TopLevelTags { get; set; }
    }
}
