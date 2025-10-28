using BepInEx.Configuration;
using UnityEngine;

namespace RevivalMod.Helpers
{
    internal class RevivalModSettings
    {
        #region Settings Properties

        // Key Bindings
        public static ConfigEntry<KeyCode> SELF_REVIVAL_KEY;
        public static ConfigEntry<KeyCode> GIVE_UP_KEY;

        // Revival Mechanics
        public static ConfigEntry<string> REVIVAL_ITEM_ID;
    public static ConfigEntry<bool> SELF_REVIVAL_ENABLED;
    public static ConfigEntry<float> SELF_REVIVE_ANIMATION_DURATION;
    public static ConfigEntry<float> TEAMMATE_REVIVE_ANIMATION_DURATION;
    public static ConfigEntry<bool> CONSUME_DEFIB_ON_TEAMMATE_REVIVE;
        public static ConfigEntry<float> REVIVAL_DURATION;
        public static ConfigEntry<float> REVIVAL_COOLDOWN;
        public static ConfigEntry<float> CRITICAL_STATE_TIME;
        public static ConfigEntry<bool> RESTORE_DESTROYED_BODY_PARTS;
        public static ConfigEntry<float> RESTORE_HEAD_PERCENTAGE;
        public static ConfigEntry<float> RESTORE_CHEST_PERCENTAGE;
        public static ConfigEntry<float> RESTORE_STOMACH_PERCENTAGE;
        public static ConfigEntry<float> RESTORE_ARMS_PERCENTAGE;
        public static ConfigEntry<float> RESTORE_LEGS_PERCENTAGE;
        public static ConfigEntry<bool> CONTUSION_EFFECT;
        public static ConfigEntry<bool> STUN_EFFECT;
        public static ConfigEntry<float> REVIVAL_RANGE;
        public static ConfigEntry<float> DOWNED_MOVEMENT_SPEED;

        // Hardcore Mode
        public static ConfigEntry<bool> GOD_MODE;
        public static ConfigEntry<bool> GHOST_MODE;
        public static ConfigEntry<bool> HARDCORE_MODE;
        public static ConfigEntry<bool> HARDCORE_HEADSHOT_DEFAULT_DEAD;
        public static ConfigEntry<float> HARDCORE_CHANCE_OF_CRITICAL_STATE;

        // Development
        public static ConfigEntry<bool> TESTING;

        #endregion

        public static void Init(ConfigFile config)
        {
            #region Key Bindings Settings

            SELF_REVIVAL_KEY = config.Bind(
                "1. Key Bindings",
                "Self Revival Key",
                KeyCode.F,
                "The key to press and hold to revive yourself when in critical state"
            );

            GIVE_UP_KEY = config.Bind(
                "1. Key Bindings",
                "Give Up Key",
                KeyCode.Backspace,
                "Press this key when in critical state to die immediately"
            );

            #endregion

            #region Revival Mechanics Settings

            REVIVAL_ITEM_ID = config.Bind(
                "2. Revival Mechanics",
                "Revival Item ID",
                "5c052e6986f7746b207bc3c9",
                "The item template ID required for revival (default is defibrillator). Common IDs: Defibrillator='5c052e6986f7746b207bc3c9', CMS='5d02778e86f774203e7dedbe', Bandage='544fb25a4bdc2dfb738b4567'"
            );

            SELF_REVIVAL_ENABLED = config.Bind(
                "2. Revival Mechanics",
                "Enable Self Revival",
                true,
                "When enabled, you can revive yourself with a defibrillator"
            );


            SELF_REVIVE_ANIMATION_DURATION = config.Bind(
                "2. Revival Mechanics",
                "Self Revive Animation Duration",
                20f,
                "Duration (seconds) used for the SurvKit animation when self-reviving"
            );

            TEAMMATE_REVIVE_ANIMATION_DURATION = config.Bind(
                "2. Revival Mechanics",
                "Teammate Revive Animation Duration",
                10f,
                "Duration (seconds) used for the CMS animation when a teammate revives you"
            );

            CONSUME_DEFIB_ON_TEAMMATE_REVIVE = config.Bind(
                "2. Revival Mechanics",
                "Consume Defib on Teammate Revive",
                false,
                "When enabled, the defibrillator will be consumed when reviving a teammate"
            );

            CRITICAL_STATE_TIME = config.Bind(
                "2. Revival Mechanics",
                "Critical State Duration",
                180f,
                "How long you can be in critical state before dying (in seconds)"
            );

            REVIVAL_DURATION = config.Bind(
                "2. Revival Mechanics",
                "Invulnerability Duration",
                4f,
                "How long you remain invulnerable after being revived (in seconds)"
            );

            REVIVAL_COOLDOWN = config.Bind(
                "2. Revival Mechanics",
                "Revival Cooldown",
                180f,
                "How long you must wait between revivals (in seconds)"
            );

            RESTORE_DESTROYED_BODY_PARTS = config.Bind(
                "2. Revival Mechanics",
                "Restore Destroyed Body Parts",
                true,
                "When enabled, destroyed body parts will be restored after revival"
            );

            RESTORE_HEAD_PERCENTAGE = config.Bind(
                "2. Revival Mechanics",
                "Head Restore Percentage",
                50f,
                "The percentage of Head's maximum health to restore (0-100)"
            );

            RESTORE_CHEST_PERCENTAGE = config.Bind(
                "2. Revival Mechanics",
                "Thorax Restore Percentage",
                50f,
                "The percentage of Thorax's maximum health to restore (0-100)"
            );

            RESTORE_STOMACH_PERCENTAGE = config.Bind(
                "2. Revival Mechanics",
                "Stomach Restore Percentage",
                50f,
                "The percentage of Stomach's maximum health to restore (0-100)"
            );

            RESTORE_ARMS_PERCENTAGE = config.Bind(
                "2. Revival Mechanics",
                "Arms Restore Percentage",
                50f,
                "The percentage of Arms' maximum health to restore (0-100)"
            );

            RESTORE_LEGS_PERCENTAGE = config.Bind(
                "2. Revival Mechanics",
                "Legs Restore Percentage",
                50f,
                "The percentage of Legs' maximum health to restore (0-100)"
            );

            CONTUSION_EFFECT = config.Bind(
                "2. Revival Mechanics",
                "Contusion effect",
                true,
                ""
            );

            STUN_EFFECT = config.Bind(
                "2. Revival Mechanics",
                "Stun effect",
                true,
                ""
            );

            REVIVAL_RANGE = config.Bind(
                "2. Revival Mechanics",
                "Revival Range",
                0.3f,
                "The interaction range for reviving downed players (requires restart raid)"
            );

            DOWNED_MOVEMENT_SPEED = config.Bind(
                "2. Revival Mechanics",
                "Downed Movement Speed",
                50f,
                "Movement speed percentage when downed (0-100, default is 50% of normal speed)"
            );

            #endregion

            #region Hardcore Mode Settings

            GOD_MODE = config.Bind(
                "3. Ghost/God Mode",
                "Enable God Mode",
                false,
                "Makes players invulnerable while in Critical State"
            );

            GHOST_MODE = config.Bind(
                "3. Ghost/God Mode",
                "Enable Ghost Mode",
                true,
                "Makes players invisible to AI while in Critical State"
            );

            HARDCORE_MODE = config.Bind(
                "3. Hardcore Mode",
                "Enable Hardcore Mode",
                false,
                "Enables hardcore mode checks for headshot instant death"
            );

            HARDCORE_HEADSHOT_DEFAULT_DEAD = config.Bind(
                "3. Hardcore Mode",
                "Headshots Are Fatal",
                false,
                "When enabled, headshots will kill you instantly without entering critical state"
            );

            HARDCORE_CHANCE_OF_CRITICAL_STATE = config.Bind(
                "3. Hardcore Mode",
                "Critical State Chance",
                0.75f,
                "Probability of entering critical state instead of dying instantly in Hardcore Mode (0.75 = 75%)"
            );

            #endregion

            #region Development Settings

            TESTING = config.Bind(
                "4. Development",
                "Test Mode",
                false,
                new ConfigDescription("Enables revival without requiring defibrillator item (for testing only)", null, new ConfigurationManagerAttributes { IsAdvanced = true })
            );

            #endregion
        }
    }
}