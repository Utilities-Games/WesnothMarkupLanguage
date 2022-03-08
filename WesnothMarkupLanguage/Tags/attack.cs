namespace WesnothMarkupLanguage.Tags
{
    /// <summary>
    /// one of the unit's attacks.
    /// </summary>
    public class attack
    {
        /// <summary>
        /// a translatable text for name of the attack, to be displayed to the user.
        /// </summary>
        public string description { get; set; }

        /// <summary>
        /// the name of the attack. Used as a default description, if description is not present, and to determine the default icon, if icon is not present (see below). Non-translatable.
        /// </summary>
        public string name { get; set; }

        // type

        // [specials]

        // icon

        // range

        /// <summary>
        /// the damage of this attack
        /// </summary>
        public int damage { get; set; }

        /// <summary>
        /// the number of strikes per attack this weapon has
        /// </summary>
        public int number { get; set; }

        /// <summary>
        /// a number added to the chance to hit whenever using this weapon offensively (i.e. during a strike with this attack, regardless of who initiated the combat); negative values work too
        /// </summary>
        public int accuracy { get; set; }

        /// <summary>
        /// a number deducted from the enemy chance to hit whenever using this weapon defensively (i.e. during the enemy's strike, regardless of who initiated the combat); negative values work too
        /// </summary>
        public int parry { get; set; }

        /// <summary>
        /// determines how many movement points using this attack expends. By default all movement is used up, set this to 0 to make attacking with this attack expend no movement.
        /// </summary>
        public int movement_used { get; set; }

        /// <summary>
        /// helps the AI to choose which attack to use when attacking, setting it to 0 disables the attack on attack. See the note about weights below.
        /// </summary>
        public int attack_weight { get; set; }

        /// <summary>
        /// used to determine which attack is used for retaliation. This affects gameplay, as the player is not allowed to determine his unit's retaliation weapon. Setting it to 0 disable the attacks on defense. See the note about weights below.
        /// </summary>
        public int defense_weight { get; set; }
    }

}
