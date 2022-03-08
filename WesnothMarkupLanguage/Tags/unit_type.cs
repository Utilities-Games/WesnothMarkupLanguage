using WesnothMarkupLanguage.Attributes;
using WesnothMarkupLanguage.Contracts.Enums;

namespace WesnothMarkupLanguage.Tags
{
    /// <summary>
    /// Defines one unit type. See <seealso href="https://wiki.wesnoth.org/UnitTypeWML"/>.
    /// </summary>
    [Tag("unit_type")]
    public class unit_type
    {
        /// <summary>
        /// the value of the type key for units of this type. This is required and must be unique among all [unit_type] tags. An id should consist only of alphanumerics and spaces (or underscores).
        /// </summary>
        public string id { get; set; }

        /// <summary>
        /// (translatable) displayed in the Status Table for units of this type.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        ///  (translatable) the text displayed in the unit descriptor box for this unit. Default 'No description available...'.
        /// </summary>
        public string description { get; set; } = "No description available...";

        /// <summary>
        /// Also used in standard unit filter (see FilterWML). Mainline Wesnoth features following values: bats, drake, dwarf, elf, falcon, goblin, gryphon, human, khalifate, lizard, mechanical, merman, monster, naga, ogre, orc, troll, undead, wolf, wose.
        /// </summary>
        public race race { get; set; }

        /// <summary>
        /// when a player recruits a unit of this type, the player loses cost gold. If this would cause gold to drop below 0, the unit cannot be recruited. Default is 1.
        /// </summary>
        public int cost { get; set; } = 1;

        /// <summary>
        /// one of lawful/neutral/chaotic/liminal (See TimeWML). Default is "neutral".
        /// </summary>
        public AlignmentType alignment { get; set; } = AlignmentType.neutral;

        /// <summary>
        /// the number of times that this unit can attack each turn. Default is 1.
        /// </summary>
        public int attacks { get; set; } = 1;

        /// <summary>
        /// has a value of either male or female, and determines which of the keys male_names and female_names should be read. When a unit of this type is recruited, it will be randomly assigned a name by the random name generator, which will use these names as a base. If gender is not specified it defaults to male.
        /// </summary>
        public GenderType gender { get; set; } = GenderType.male;

        /// <summary>
        /// When this unit has experience greater than or equal to experience, it is replaced by a unit with 0 experience of the type that the value of advances_to refers to. All modifications that have been done to the unit are applied to the unit it is replaced by.
        /// </summary>
        public int experience { get; set; }

        /// <summary>
        /// the maximum HP that the unit has, and the HP it has when it is created.
        /// </summary>
        public int hitpoints { get; set; }

        /// <summary>
        /// the number of move points that this unit receives each turn.
        /// </summary>
        public int movement { get; set; }

        /// <summary>
        ///  the amount of upkeep the unit costs. After this unit fights, its opponent gains level experience.
        /// </summary>
        public int level { get; set; }

        /// <summary>
        ///  the amount of upkeep the unit costs if it differs from its level.
        /// </summary>
        public int upkeep { get; set; }

        // advances_to

        // recall_cost

        // do_not_list

        // ellipse

        // flag_rgb

        // halo
        
        // hide_help

        // ignore_race_traits

        // image

        // image_icon

        // movement_type

        // num_traits

        // profile

        // small_profile

        // undead_variation

        // usage

        // vision
        
        // jamming

        // zoc

        // die_sound

        // healed_sound

        // hp_bar_scaling

        // xp_bar_scaling

        // bar_offset_x

        // bar_offset_y

        // [[advancements]]

        // [[attacks]]

        // [base_unit]

        // [abilities]

        // [event]

        // [variation]

        // [male]

        // [female]

        // [special_note]
    }

}
