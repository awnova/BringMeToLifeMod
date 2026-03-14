using BepInEx.Configuration;
using UnityEngine;

namespace KeepMeAlive.Helpers
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
        public static ConfigEntry<float> CRITICAL_STATE_TIME;
        // Self-Revive Post-Revival
        public static ConfigEntry<bool>  SELF_REVIVE_RESTORE_BODY_PARTS;
        public static ConfigEntry<float> SELF_REVIVE_HEAD_PCT;
        public static ConfigEntry<float> SELF_REVIVE_CHEST_PCT;
        public static ConfigEntry<float> SELF_REVIVE_STOMACH_PCT;
        public static ConfigEntry<float> SELF_REVIVE_ARMS_PCT;
        public static ConfigEntry<float> SELF_REVIVE_LEGS_PCT;
        public static ConfigEntry<bool>  SELF_REVIVE_REMOVE_BLEEDS;
        public static ConfigEntry<bool>  SELF_REVIVE_REMOVE_FRACTURES;
        public static ConfigEntry<float> SELF_REVIVE_INVULN_DURATION;
        public static ConfigEntry<float> SELF_REVIVE_INVULN_SPEED_PCT;
        public static ConfigEntry<float> SELF_REVIVE_COOLDOWN;
        public static ConfigEntry<bool>  SELF_REVIVE_CONTUSION_ON_REVIVE;
        public static ConfigEntry<float> SELF_REVIVE_CONTUSION_DURATION;
        public static ConfigEntry<bool>  SELF_REVIVE_PAIN_ON_REVIVE;
        // Team-Revive Post-Revival
        public static ConfigEntry<bool>  TEAM_REVIVE_RESTORE_BODY_PARTS;
        public static ConfigEntry<float> TEAM_REVIVE_HEAD_PCT;
        public static ConfigEntry<float> TEAM_REVIVE_CHEST_PCT;
        public static ConfigEntry<float> TEAM_REVIVE_STOMACH_PCT;
        public static ConfigEntry<float> TEAM_REVIVE_ARMS_PCT;
        public static ConfigEntry<float> TEAM_REVIVE_LEGS_PCT;
        public static ConfigEntry<bool>  TEAM_REVIVE_REMOVE_BLEEDS;
        public static ConfigEntry<bool>  TEAM_REVIVE_REMOVE_FRACTURES;
        public static ConfigEntry<float> TEAM_REVIVE_INVULN_DURATION;
        public static ConfigEntry<float> TEAM_REVIVE_INVULN_SPEED_PCT;
        public static ConfigEntry<float> TEAM_REVIVE_COOLDOWN;
        public static ConfigEntry<bool>  TEAM_REVIVE_CONTUSION_ON_REVIVE;
        public static ConfigEntry<float> TEAM_REVIVE_CONTUSION_DURATION;
        public static ConfigEntry<bool>  TEAM_REVIVE_PAIN_ON_REVIVE;
        public static ConfigEntry<bool> CONTUSION_EFFECT;
        public static ConfigEntry<bool> STUN_EFFECT;
        public static ConfigEntry<float> MEDICAL_RANGE;
        public static ConfigEntry<float> DOWNED_MOVEMENT_SPEED;
        // Hardcore Mode
        public static ConfigEntry<bool> DEATH_BLOCK_IN_CRITICAL;
        public static ConfigEntry<bool> GOD_MODE;
        public static ConfigEntry<bool> GHOST_MODE;
        public static ConfigEntry<bool> HARDCORE_MODE;
        public static ConfigEntry<bool> HARDCORE_HEADSHOT_DEFAULT_DEAD;
        public static ConfigEntry<float> HARDCORE_CHANCE_OF_CRITICAL_STATE;

        // Team Healing
        public static ConfigEntry<float> TEAM_HEAL_HOLD_TIME;
        public static ConfigEntry<float> TEAM_HEAL_MIN_HP_RESOURCE;

        // Development
        public static ConfigEntry<bool> NO_DEFIB_REQUIRED;
        public static ConfigEntry<bool> DEBUG_KEYBINDS;
        public static ConfigEntry<bool> FREE_TEAM_HEALING;

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

            CONTUSION_EFFECT = config.Bind(
                "2. Revival Mechanics",
                "Contusion effect",
                true,
                "When enabled, applies a contusion effect when entering critical state"
            );

            STUN_EFFECT = config.Bind(
                "2. Revival Mechanics",
                "Stun effect",
                true,
                "When enabled, applies a stun effect when entering critical state"
            );

            MEDICAL_RANGE = config.Bind(
                "2. Revival Mechanics",
                "Revival Range",
                .5f,
                "The interaction range for reviving downed players in meters (requires restart raid)"
            );

            DOWNED_MOVEMENT_SPEED = config.Bind(
                "2. Revival Mechanics",
                "Downed Movement Speed",
                50f,
                "Movement speed percentage when downed (0-100, default is 50% of normal speed)"
            );

            #endregion

            #region Post-Revival Effects Settings

            SELF_REVIVE_RESTORE_BODY_PARTS = config.Bind(
                "3. Post-Revival Effects",
                "Self: Restore Destroyed Body Parts",
                true,
                "When enabled, destroyed body parts are restored after a self-revive"
            );

            SELF_REVIVE_HEAD_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Self: Head Restore Percentage",
                0f,
                "Percentage of Head's maximum health to restore on self-revive (0-100)"
            );

            SELF_REVIVE_CHEST_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Self: Thorax Restore Percentage",
                35f,
                "Percentage of Thorax's maximum health to restore on self-revive (0-100)"
            );

            SELF_REVIVE_STOMACH_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Self: Stomach Restore Percentage",
                35f,
                "Percentage of Stomach's maximum health to restore on self-revive (0-100)"
            );

            SELF_REVIVE_ARMS_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Self: Arms Restore Percentage",
                35f,
                "Percentage of Arms' maximum health to restore on self-revive (0-100)"
            );

            SELF_REVIVE_LEGS_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Self: Legs Restore Percentage",
                35f,
                "Percentage of Legs' maximum health to restore on self-revive (0-100)"
            );

            SELF_REVIVE_REMOVE_BLEEDS = config.Bind(
                "3. Post-Revival Effects",
                "Self: Remove Bleeds on Revive",
                false,
                "When enabled, all active bleeds are cleared when you self-revive"
            );

            SELF_REVIVE_REMOVE_FRACTURES = config.Bind(
                "3. Post-Revival Effects",
                "Self: Remove Fractures on Revive",
                false,
                "When enabled, fractures on arms and legs are cleared when you self-revive"
            );

            SELF_REVIVE_INVULN_DURATION = config.Bind(
                "3. Post-Revival Effects",
                "Self: Invulnerability Duration",
                3f,
                "Seconds of god-mode invulnerability after self-revive"
            );

            SELF_REVIVE_INVULN_SPEED_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Self: Invulnerability Speed Percent",
                100f,
                "Movement speed percentage during self-revive invulnerability (100 = normal, any numeric value allowed)"
            );

            SELF_REVIVE_COOLDOWN = config.Bind(
                "3. Post-Revival Effects",
                "Self: Revival Cooldown",
                240f,
                "Seconds before you can be revived again after a self-revive"
            );

            SELF_REVIVE_CONTUSION_ON_REVIVE = config.Bind(
                "3. Post-Revival Effects",
                "Self: Apply Contusion on Revive",
                true,
                "When enabled, applies a contusion (screen blur/deafen) when standing up after self-revive"
            );

            SELF_REVIVE_CONTUSION_DURATION = config.Bind(
                "3. Post-Revival Effects",
                "Self: Contusion Duration",
                10f,
                "Duration in seconds of the post-revive contusion effect after self-revive"
            );

            SELF_REVIVE_PAIN_ON_REVIVE = config.Bind(
                "3. Post-Revival Effects",
                "Self: Apply Pain on Revive",
                true,
                "When enabled, applies a pain effect (hand shake/sway) when standing up after self-revive"
            );

            TEAM_REVIVE_RESTORE_BODY_PARTS = config.Bind(
                "3. Post-Revival Effects",
                "Team: Restore Destroyed Body Parts",
                true,
                "When enabled, destroyed body parts are restored after a teammate revive"
            );

            TEAM_REVIVE_HEAD_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Team: Head Restore Percentage",
                50f,
                "Percentage of Head's maximum health to restore on teammate revive (0-100)"
            );

            TEAM_REVIVE_CHEST_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Team: Thorax Restore Percentage",
                50f,
                "Percentage of Thorax's maximum health to restore on teammate revive (0-100)"
            );

            TEAM_REVIVE_STOMACH_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Team: Stomach Restore Percentage",
                50f,
                "Percentage of Stomach's maximum health to restore on teammate revive (0-100)"
            );

            TEAM_REVIVE_ARMS_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Team: Arms Restore Percentage",
                50f,
                "Percentage of Arms' maximum health to restore on teammate revive (0-100)"
            );

            TEAM_REVIVE_LEGS_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Team: Legs Restore Percentage",
                50f,
                "Percentage of Legs' maximum health to restore on teammate revive (0-100)"
            );

            TEAM_REVIVE_REMOVE_BLEEDS = config.Bind(
                "3. Post-Revival Effects",
                "Team: Remove Bleeds on Revive",
                true,
                "When enabled, all active bleeds are cleared when revived by a teammate"
            );

            TEAM_REVIVE_REMOVE_FRACTURES = config.Bind(
                "3. Post-Revival Effects",
                "Team: Remove Fractures on Revive",
                true,
                "When enabled, fractures on arms and legs are cleared when revived by a teammate"
            );

            TEAM_REVIVE_INVULN_DURATION = config.Bind(
                "3. Post-Revival Effects",
                "Team: Invulnerability Duration",
                5f,
                "Seconds of god-mode invulnerability after teammate revive"
            );

            TEAM_REVIVE_INVULN_SPEED_PCT = config.Bind(
                "3. Post-Revival Effects",
                "Team: Invulnerability Speed Percent",
                100f,
                "Movement speed percentage during teammate-revive invulnerability (100 = normal, any numeric value allowed)"
            );

            TEAM_REVIVE_COOLDOWN = config.Bind(
                "3. Post-Revival Effects",
                "Team: Revival Cooldown",
                180f,
                "Seconds before you can be revived again after a teammate revive"
            );

            TEAM_REVIVE_CONTUSION_ON_REVIVE = config.Bind(
                "3. Post-Revival Effects",
                "Team: Apply Contusion on Revive",
                true,
                "When enabled, applies a contusion (screen blur/deafen) when standing up after teammate revive"
            );

            TEAM_REVIVE_CONTUSION_DURATION = config.Bind(
                "3. Post-Revival Effects",
                "Team: Contusion Duration",
                5f,
                "Duration in seconds of the post-revive contusion effect after teammate revive"
            );

            TEAM_REVIVE_PAIN_ON_REVIVE = config.Bind(
                "3. Post-Revival Effects",
                "Team: Apply Pain on Revive",
                false,
                "When enabled, applies a pain effect (hand shake/sway) when standing up after teammate revive"
            );

            #endregion

            #region Hardcore Mode Settings

            // Death blocking: Prevents Kill() calls during critical state (separate from damage)
            DEATH_BLOCK_IN_CRITICAL = config.Bind(
                "3. Protection Settings",
                "Block Death in Critical State",
                true,
                "When enabled, prevents death from additional damage/kills during BleedingOut/Reviving states. " +
                "Player can still take damage if God Mode is disabled, but won't die."
            );

            // God Mode: Prevents damage from being applied (HP doesn't decrease)
            GOD_MODE = config.Bind(
                "3. Protection Settings",
                "Enable God Mode (Damage Immunity)",
                false,
                "When enabled, prevents all damage during BleedingOut/Reviving states (SetDamageCoeff = 0). " +
                "HP will not decrease. Always active during Revived state regardless of this setting."
            );

            // Ghost Mode: Makes player invisible to AI
            GHOST_MODE = config.Bind(
                "3. Protection Settings",
                "Enable Ghost Mode",
                true,
                "Makes players invisible to AI during BleedingOut/Reviving states. " +
                "Never active during Revived state."
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

            #region Team Healing Settings

            TEAM_HEAL_HOLD_TIME = config.Bind(
                "4. Team Healing",
                "Hold Time",
                3f,
                "Duration in seconds the healer must hold to apply a med to a teammate"
            );

            TEAM_HEAL_MIN_HP_RESOURCE = config.Bind(
                "4. Team Healing",
                "Min HP Resource to Display",
                50f,
                "Health-category medkits with less than this much HP resource remaining will not be shown in the teammate heal picker (0 = show all non-empty kits)"
            );

            #endregion

            #region Development Settings

            NO_DEFIB_REQUIRED = config.Bind(
                "5. Development",
                "No Defib Required",
                false,
                new ConfigDescription("Bypasses defibrillator requirement for all revivals (for testing only)", null, new ConfigurationManagerAttributes { IsAdvanced = true })
            );

            DEBUG_KEYBINDS = config.Bind(
                "5. Development",
                "Debug Keybinds",
                false,
                new ConfigDescription("Enables debug keybinds: F3=SurvKit, F4=CMS, F7=Enter Ghost Mode, F8=Exit Ghost Mode", null, new ConfigurationManagerAttributes { IsAdvanced = true })
            );

            FREE_TEAM_HEALING = config.Bind(
                "5. Development",
                "Free Team Healing",
                false,
                new ConfigDescription("Allows team healing without requiring medical items in inventory (for testing only)", null, new ConfigurationManagerAttributes { IsAdvanced = true })
            );

            #endregion
        }
    }
}