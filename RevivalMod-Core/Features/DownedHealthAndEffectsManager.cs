using System;
using System.Collections;
using EFT;
using EFT.HealthSystem;
using KeepMeAlive.Components;
using KeepMeAlive.Helpers;
using UnityEngine;

namespace KeepMeAlive.Features
{
    /// <summary>
    /// Manages health restoration, critical visual/audio effects, and awareness for downed players.
    /// </summary>
    internal static class DownedHealthAndEffectsManager
    {
        private static readonly EBodyPart[] TrackedBodyParts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach,
            EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg
        };

        /// <summary>Restore all destroyed body parts to 1 HP so the player doesn't instantly die.</summary>
        public static void RestoreVitalsToMinimum(Player player)
        {
            if (player?.ActiveHealthController is not { } hc) return;
            try
            {
                for (int i = 0; i < TrackedBodyParts.Length; i++)
                {
                    var part = TrackedBodyParts[i];
                    if (hc.IsBodyPartDestroyed(part) && hc.FullRestoreBodyPart(part))
                    {
                        float delta = 1f - hc.GetBodyPartHealth(part).Current;
                        if (delta < -0.01f) hc.ChangeHealth(part, delta, default);
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedHealthAndEffects] RestoreVitalsToMinimum error: {ex.Message}"); }
        }

        /// <summary>Apply stun/contusion screen effects and store original movement speed.</summary>
        public static void ApplyCriticalEffects(Player player)
        {
            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);
                PlayerRestorations.StoreOriginalMovementSpeed(player);

                if (player?.ActiveHealthController != null)
                {
                    if (RevivalModSettings.CONTUSION_EFFECT.Value) player.ActiveHealthController.DoContusion(RevivalModSettings.CRITICAL_STATE_TIME.Value, 1f);
                    if (RevivalModSettings.STUN_EFFECT.Value) player.ActiveHealthController.DoStun(Math.Min(RevivalModSettings.CRITICAL_STATE_TIME.Value, 20f), 1f);
                }

                DownedMovementController.ApplyDownedMovementSpeed(player, st);
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedHealthAndEffects] ApplyCriticalEffects error: {ex.Message}"); }
        }

        /// <summary>Restore awareness if it was previously stored.</summary>
        public static void RemoveRevivableState(Player player)
        {
            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);
                if (st.HasStoredAwareness) PlayerRestorations.RestoreAwareness(player);
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedHealthAndEffects] RemoveRevivableState error: {ex.Message}"); }
        }
    }
}
