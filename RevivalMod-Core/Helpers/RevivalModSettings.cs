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
        public static ConfigEntry<KeyCode> SURVKIT_TEST_KEY;

        // Revival Mechanics
        public static ConfigEntry<bool> SELF_REVIVAL_ENABLED;
        public static ConfigEntry<float> REVIVAL_HOLD_DURATION;
        public static ConfigEntry<float> TEAM_REVIVAL_HOLD_DURATION;
        public static ConfigEntry<float> REVIVAL_DURATION;
        public static ConfigEntry<float> REVIVAL_COOLDOWN;
        public static ConfigEntry<float> CRITICAL_STATE_TIME;
        public static ConfigEntry<bool> RESTORE_DESTROYED_BODY_PARTS;
        public static ConfigEntry<float> RESTORE_DESTROYED_BODY_PARTS_AMOUNT;
        public static ConfigEntry<bool> CONTUSION_EFFECT;
        public static ConfigEntry<bool> STUN_EFFECT;
        public static ConfigEntry<float> REVIVAL_RANGE_X;
        public static ConfigEntry<float> REVIVAL_RANGE_Y; 
        public static ConfigEntry<float> REVIVAL_RANGE_Z;

        // Hardcore Mode
        public static ConfigEntry<bool> PLAYER_ALIVE;
        public static ConfigEntry<bool> GOD_MODE;
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

            SURVKIT_TEST_KEY = config.Bind(
                "1. Key Bindings",
                "SurvKit Animation Test Key",
                KeyCode.F4,
                "Press this key to trigger SurvKit animation test (bypasses inventory check)"
            );

            #endregion

            #region Revival Mechanics Settings

            SELF_REVIVAL_ENABLED = config.Bind(
                "2. Revival Mechanics",
                "Enable Self Revival",
                true,
                "When enabled, you can revive yourself with a defibrillator"
            );

            REVIVAL_HOLD_DURATION = config.Bind(
                "2. Revival Mechanics",
                "Self Revival Hold Duration",
                3f,
                "How many seconds you need to hold the Self Revival Key to revive yourself"
            );

            TEAM_REVIVAL_HOLD_DURATION = config.Bind(
                "2. Revival Mechanics",
                "Team Revival Hold Duration",
                5f,
                "How many seconds you need to hold the Team Revival Key to revive a teammate"
            );

            CRITICAL_STATE_TIME = config.Bind(
                "2. Revival Mechanics",
                "Critical State Duration",
                180f,
                "How long you remain in critical state before dying (in seconds)"
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

            RESTORE_DESTROYED_BODY_PARTS_AMOUNT = config.Bind(
                "2. Revival Mechanics",
                "Restore Destroyed Body Parts percentage",
                0f,
                "The percentage of Body Part's health to be restored (i.e 50%)"
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

            PLAYER_ALIVE = config.Bind(
                "3. Ghost/God Mode",
                "Enable Ghost Mode",
                true,  // Changed from false to true
                "Makes players not targetable by AI while in critical state."
            );

            GOD_MODE = config.Bind(
                "3. Ghost/God Mode",
                "Enable God Mode",
                false,
                "Makes players invulnerable while in Critical State"
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