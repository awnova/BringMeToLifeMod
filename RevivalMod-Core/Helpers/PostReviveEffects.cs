//====================[ Imports ]====================
using System;
using EFT;
using EFT.HealthSystem;
using KeepMeAlive.Components;

namespace KeepMeAlive.Helpers
{
    //====================[ DefibCooldown Effect Types ]====================
    internal interface IDefibCooldown : IEffect { }
    internal class DefibCooldownEffect : ActiveHealthController.GClass3008, IDefibCooldown { }
    internal class DefibCooldownNetworkEffect : NetworkHealthControllerAbstractClass.NetworkBodyEffectsAbstractClass, IDefibCooldown { }

    //====================[ PostReviveEffects ]====================
    // Single entry point for everything that happens to a player the moment revival completes.
    // Called by DownedStateController.FinishRevive (authoritative path) and by the Fika
    // packet handlers (OnRevivedPacket / resync) for remote-player / edge-case paths.
    internal static class PostReviveEffects
    {
        // Fractures only ever attach to limbs — Head/Chest/Stomach never fracture in EFT.
        private static readonly EBodyPart[] FractureBodyParts =
        {
            EBodyPart.LeftArm, EBodyPart.RightArm,
            EBodyPart.LeftLeg, EBodyPart.RightLeg
        };

        //====================[ Public Entry Points ]====================
        /// <summary>
        /// Apply all post-revival effects based on how the player was revived.
        /// Safe to call on the local player only; health changes must never run on
        /// observed players to avoid HealthSync desyncs.
        /// </summary>
        /// <param name="applyDebuffs">
        /// Pass false when calling from resync/late-join paths to avoid re-applying
        /// contusion and pain to a player who is already standing.
        /// </param>
        public static void Apply(Player player, ReviveSource source, bool applyDebuffs = true)
        {
            if (player?.ActiveHealthController == null) return;

            bool isSelf = source == ReviveSource.Self;

            try { RestoreBodyParts(player, isSelf); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[PostReviveEffects] RestoreBodyParts error: {ex.Message}"); }

            try { RemoveBleeds(player, isSelf); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[PostReviveEffects] RemoveBleeds error: {ex.Message}"); }

            try { RemoveFractures(player, isSelf); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[PostReviveEffects] RemoveFractures error: {ex.Message}"); }

            if (applyDebuffs)
            {
                try { ApplyDebuffs(player, isSelf); }
                catch (Exception ex) { Plugin.LogSource.LogError($"[PostReviveEffects] ApplyDebuffs error: {ex.Message}"); }
            }
        }

        /// <summary>Duration of god-mode invulnerability after revival, per source.</summary>
        public static float GetInvulnDuration(ReviveSource source)
            => source == ReviveSource.Self
                ? RevivalModSettings.SELF_REVIVE_INVULN_DURATION.Value
                : RevivalModSettings.TEAM_REVIVE_INVULN_DURATION.Value;

        /// <summary>Revival cooldown length after invuln ends, per source.</summary>
        public static float GetCooldownDuration(ReviveSource source)
            => source == ReviveSource.Self
                ? RevivalModSettings.SELF_REVIVE_COOLDOWN.Value
                : RevivalModSettings.TEAM_REVIVE_COOLDOWN.Value;

        /// <summary>
        /// Movement speed multiplier during invulnerability, per source.
        /// Config values are percentages where 100 = normal speed.
        /// Values are used as-is so any numeric config value is honored.
        /// </summary>
        public static float GetInvulnSpeedMultiplier(ReviveSource source)
        {
            float pct = source == ReviveSource.Self
                ? RevivalModSettings.SELF_REVIVE_INVULN_SPEED_PCT.Value
                : RevivalModSettings.TEAM_REVIVE_INVULN_SPEED_PCT.Value;

            if (float.IsNaN(pct) || float.IsInfinity(pct)) return 1f;
            return pct / 100f;
        }

        //====================[ Body Part Restoration ]====================
        private static void RestoreBodyParts(Player player, bool isSelf)
        {
            bool enabled = isSelf
                ? RevivalModSettings.SELF_REVIVE_RESTORE_BODY_PARTS.Value
                : RevivalModSettings.TEAM_REVIVE_RESTORE_BODY_PARTS.Value;

            if (!enabled) return;

            var hc = player.ActiveHealthController;
            if (hc == null) return;

            foreach (EBodyPart part in Enum.GetValues(typeof(EBodyPart)))
            {
                if (part == EBodyPart.Common) continue;
                // DO NOT check IsBodyPartDestroyed here! 
                // Vitals like Stomach might be saved at exactly 1 HP by the downed state,
                // so they are not formally "destroyed" but still need to be restored to their target percentages.
                RestoreOneBodyPart(hc, part, isSelf);
            }
        }

        private static void RestoreOneBodyPart(ActiveHealthController hc, EBodyPart part, bool isSelf)
        {
            try
            {
                bool isDestroyed = hc.IsBodyPartDestroyed(part);
                if (isDestroyed && !hc.FullRestoreBodyPart(part)) return;

                var current = hc.GetBodyPartHealth(part);
                float pct   = GetRestorePercent(part, isSelf);
                float newHp = current.Maximum * pct;
                float delta = newHp - current.Current;

                // Only apply if it's giving us back health up to the target percentage
                if (delta > 0.01f)
                    hc.ChangeHealth(part, delta, default);

                // If the limb was utterly destroyed, we mimic a real CMS/SurvKit and wipe ALL negative effects
                // on this specific limb right away. (If it wasn't destroyed, we leave it alone and let the
                // config-driven RemoveBleeds/RemoveFractures methods handle it below).
                if (isDestroyed)
                {
                    hc.RemoveNegativeEffects(part);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PostReviveEffects] RestoreOneBodyPart {part} error: {ex.Message}");
            }
        }

        private static float GetRestorePercent(EBodyPart part, bool isSelf) => part switch
        {
            EBodyPart.Head    => (isSelf ? RevivalModSettings.SELF_REVIVE_HEAD_PCT    : RevivalModSettings.TEAM_REVIVE_HEAD_PCT).Value    / 100f,
            EBodyPart.Chest   => (isSelf ? RevivalModSettings.SELF_REVIVE_CHEST_PCT   : RevivalModSettings.TEAM_REVIVE_CHEST_PCT).Value   / 100f,
            EBodyPart.Stomach => (isSelf ? RevivalModSettings.SELF_REVIVE_STOMACH_PCT : RevivalModSettings.TEAM_REVIVE_STOMACH_PCT).Value / 100f,
            EBodyPart.LeftArm  or EBodyPart.RightArm => (isSelf ? RevivalModSettings.SELF_REVIVE_ARMS_PCT : RevivalModSettings.TEAM_REVIVE_ARMS_PCT).Value / 100f,
            EBodyPart.LeftLeg  or EBodyPart.RightLeg => (isSelf ? RevivalModSettings.SELF_REVIVE_LEGS_PCT : RevivalModSettings.TEAM_REVIVE_LEGS_PCT).Value / 100f,
            _ => 0.5f
        };

        //====================[ Effect Removal ]====================
        /// <summary>
        /// Removes all active bleeds (light and heavy) from every body part.
        /// Uses EFT's internal method_19 to wipe all subclasses of Bleeding PLUS 'Wound' (Fresh Wound).
        /// </summary>
        private static void RemoveBleeds(Player player, bool isSelf)
        {
            bool enabled = isSelf
                ? RevivalModSettings.SELF_REVIVE_REMOVE_BLEEDS.Value
                : RevivalModSettings.TEAM_REVIVE_REMOVE_BLEEDS.Value;

            if (!enabled) return;

            var hc = player.ActiveHealthController;
            if (hc == null) return;

            // Remove all standard heavy/light bleeds
            hc.method_17(EBodyPart.Common); 
        }

        /// <summary>
        /// Removes fractures from limb body parts.
        /// </summary>
        private static void RemoveFractures(Player player, bool isSelf)
        {
            bool enabled = isSelf
                ? RevivalModSettings.SELF_REVIVE_REMOVE_FRACTURES.Value
                : RevivalModSettings.TEAM_REVIVE_REMOVE_FRACTURES.Value;

            var hc = player.ActiveHealthController;
            if (hc == null) return;

            for (int i = 0; i < FractureBodyParts.Length; i++)
                hc.RemoveNegativeEffects(FractureBodyParts[i]);
        }

        //====================[ Post-Revival Debuffs ]====================
        private static void ApplyDebuffs(Player player, bool isSelf)
        {
            var hc = player.ActiveHealthController;
            if (hc == null) return;

            bool doContusion = isSelf
                ? RevivalModSettings.SELF_REVIVE_CONTUSION_ON_REVIVE.Value
                : RevivalModSettings.TEAM_REVIVE_CONTUSION_ON_REVIVE.Value;

            if (doContusion)
            {
                float dur = isSelf
                    ? RevivalModSettings.SELF_REVIVE_CONTUSION_DURATION.Value
                    : RevivalModSettings.TEAM_REVIVE_CONTUSION_DURATION.Value;

                hc.DoContusion(dur, 1f);
            }

            bool doPain = isSelf
                ? RevivalModSettings.SELF_REVIVE_PAIN_ON_REVIVE.Value
                : RevivalModSettings.TEAM_REVIVE_PAIN_ON_REVIVE.Value;

            if (doPain)
            {
                // method_27 = DoPain(bodyPart, delayTime, workTime, residueTime, strength?)
                // Applied to head; full-body sway/hands shake at default strength.
                hc.method_27(EBodyPart.Head, 0f, 30f, 15f);
            }
        }

        //====================[ Cooldown Effect ]====================
        /// <summary>
        /// Applies the DefibCooldown status effect icon for the given cooldown duration.
        /// AddEffect fires EffectStartedEvent on the local health controller, which Fika
        /// intercepts and broadcasts as a health sync packet so all peers show the icon too.
        /// Must only be called on the local player (IsYourPlayer).
        /// </summary>
        public static void ApplyCooldownEffect(Player player, float cooldownDuration)
        {
            if (player?.ActiveHealthController == null)
            {
                Plugin.LogSource.LogWarning("[PostReviveEffects] ApplyCooldownEffect: ActiveHealthController is null, skipping.");
                return;
            }
            try
            {
                Plugin.LogSource.LogDebug(
                    $"[PostReviveEffects] Applying DefibCooldown effect: hc={player.ActiveHealthController.GetType().Name}, workTime={cooldownDuration}");
                var effect = player.ActiveHealthController.AddEffect<DefibCooldownEffect>(
                    EBodyPart.Chest,
                    delayTime: null,
                    workTime: cooldownDuration,
                    residueTime: 3f);
                Plugin.LogSource.LogDebug(
                    $"[PostReviveEffects] DefibCooldown applied: {effect?.GetType().Name}, Type={effect?.Type?.Name}, State={effect?.State}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PostReviveEffects] ApplyCooldownEffect error: {ex}");
            }
        }
    }
}
