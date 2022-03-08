namespace WesnothMarkupLanguage.Tags
{
    /// <summary>
    /// describes what happens to a unit when it reaches the XP required for advancement. It is considered as an advancement in the same way as advancement described by advances_to; however, if the player chooses this advancement, the unit will have one or more effects applied to it instead of advancing.
    /// </summary>
    public class advancement
    {
        /// <summary>
        /// unique identifier for this advancement; Required if there are multiple advancement options, or if strict_amla=no.
        /// </summary>
        public string id { get; set; }

        /// <summary>
        /// if set to true displays the AMLA option even if it is the only available one.
        /// </summary>
        public bool always_display { get; set; }

        /// <summary>
        /// a description displayed as the option for this advancement if there is another advancement option that the player must choose from; otherwise, the advancement is chosen automatically and this key is irrelevant.
        /// </summary>
        public string description { get; set; }

        /// <summary>
        /// an image to display next to the description in the advancement menu.
        /// </summary>
        public string image { get; set; }

        /// <summary>
        /// default 1. The maximum times the unit can be awarded this advancement. Pass -1 for "unlimited".
        /// </summary>
        public int max_times { get; set; } = 1;

        /// <summary>
        /// (yes|no) default=no. Disable the AMLA if the unit can advance to another unit.
        /// </summary>
        public bool strict_amla { get; set; } = false;

        /// <summary>
        /// (yes|no) default=no. Sets whether the unit's XP bar is blue(=yes) or purple(=no). In case of more [advancement] tags, if there is one with major_amla=yes, the XP bar will be blue.
        /// </summary>
        public bool major_amla { get; set; } = false;

        // require_amla

        // exclude_amla

        // [effect]

        // [filter]
    }

}
