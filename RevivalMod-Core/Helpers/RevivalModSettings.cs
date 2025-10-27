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
    public static ConfigEntry<bool> SELF_REVIVAL_ENABLED;
    public static ConfigEntry<float> SELF_REVIVE_ANIMATION_DURATION;
    public static ConfigEntry<float> TEAMMATE_REVIVE_ANIMATION_DURATION;
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
        public static ConfigEntry<float> REVIVAL_RANGE_X;
        public static ConfigEntry<float> REVIVAL_RANGE_Y; 
        public static ConfigEntry<float> REVIVAL_RANGE_Z;

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
                KeyCode.F5,
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
                false,
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

            REVIVAL_RANGE_X = config.Bind(
                "2. Revival Mechanics",
                "Hitbox X dimension (requires restart raid)",
                0.3f,
                ""
            );

            REVIVAL_RANGE_Y = config.Bind(
                "2. Revival Mechanics",
                "Hitbox Y dimension (requires restart raid)",
                0.3f,
                ""
            );

            REVIVAL_RANGE_Z = config.Bind(
                "2. Revival Mechanics",
                "Hitbox Z dimension (requires restart raid)",
                0.3f,
                ""
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