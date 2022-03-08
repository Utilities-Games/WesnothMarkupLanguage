using WesnothMarkupLanguage.Attributes;

namespace WesnothMarkupLanguage.Tags
{
    [Tag("race")]
    public class race
    {
        /// <summary>
        ///  ID for this race. Units with the attribute race=id will be assigned this race. In older versions of WML, the value of the name key was used as id if the id field was missing, but this is no longer the case.
        /// </summary>
        public string id { get; set; }

        /// <summary>
        /// user-visible name for its people (e.g. "Merfolk" or "Elves"). Currently only used in the in-game help.
        /// </summary>
        public string plural_name { get; set; }

        /// <summary>
        /// user-visible name for the race of the male units (e.g. "Merman"). Currently only used in the in-game unit status.
        /// </summary>
        public string male_name { get; set; }

        /// <summary>
        /// user-visible name for the race of the female units (e.g. "Mermaid"). Currently only used in the in-game unit status.
        /// </summary>
        public string female_name { get; set; }

        /// <summary>
        /// the default value for the three keys above. The 'name' key is the default for 'male_name' and 'female_name'. 'id' and 'plural_name' must be supplied.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// text used in the in-game help.
        /// </summary>
        public string description { get; set; }

        // help_taxonomy

        // name_generator

        // male_name_generator

        // female_name_generator

        // male_names

        // female_names

        // markov_chain_size

        // num_traits

        // ignore_global_traits

        // undead_variation

        // [topic]

        // [trait]

        // [[resistance_defaults]]

        // [[terrain_defaults]]

        // [[hide_help]]
    }

}
