using System.Collections.Generic;
using WesnothMarkupLanguage.Contracts;

namespace WesnothMarkupLanguage
{
    public class Config : IConfig
    {
        public List<IPreprocessorDirective> Directives { get; set; } = new List<IPreprocessorDirective>();
        public List<ITag> TopLevelTags { get; set; } = new List<ITag>();
    }
}
