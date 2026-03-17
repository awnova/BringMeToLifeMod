//====================[ Imports ]====================
using System;
using System.Collections;
using EFT;
using KeepMeAlive.Components;
using KeepMeAlive.Helpers;
using UnityEngine;

namespace KeepMeAlive.Features
{
    //====================[ DownedMovementController ]====================
    internal static class DownedMovementController
    {
        //====================[ Public API ]====================
        // Force the player into a revivable state: prone, no sprint, empty hands, and unhooked movement listeners.
        public static void ApplyRevivableState(Player player)
        {
            try
            {
                PlayerRestorations.SetAwarenessZero(player);
                Plugin.StaticCoroutineRunner.StartCoroutine(DeferredSetEmptyHands(player));

                var mc = player.MovementContext;
                mc.EnableSprint(false);
                mc.SetPoseLevel(0f, true);
                mc.IsInPronePose = true;

                if (player.ShouldVocalizeDeath(player.LastDamagedBodyPart))
                {
                    var trig = player.LastDamageType.IsWeaponInduced() ? EPhraseTrigger.OnDeath : EPhraseTrigger.OnAgony;
                    try { player.Speaker.Play(trig, player.HealthStatus, true, null); } catch { }
                }

                mc.ReleaseDoorIfInteractingWithOne();
                mc.OnStateChanged -= player.method_17;
                mc.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;

                if (mc.StationaryWeapon != null)
                {
                    mc.StationaryWeapon.Unlock(player.ProfileId);
                    if (mc.StationaryWeapon.Item == player.HandsController.Item)
                    {
                        mc.StationaryWeapon.Show();
                        player.ReleaseHand();
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedMovement] ApplyRevivableState error: {ex.Message}"); }
        }

        // Re-subscribe movement and animation event hooks that were stripped when the player went down.
        public static void ReattachMovementHooks(Player player)
        {
            if (player.MovementContext == null) return;
            try
            {
                var mc = player.MovementContext;
                mc.OnStateChanged -= player.method_17;
                mc.OnStateChanged += player.method_17;
                mc.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
                mc.PhysicalConditionChanged += player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[DownedMovement] Re-hook movement events error: {ex.Message}"); }
        }

        // Scale walk speed from downed settings, or freeze movement while revive flow is active.
        public static void ApplyDownedMovementSpeed(Player player, RMPlayer st)
        {
            try
            {
                bool frozen = st.State == RMState.Reviving || st.IsBeingRevived || st.IsSelfReviving || st.SelfReviveAuthPending || st.SelfReviveHoldTime > 0f;
                float baseSpd = st.OriginalMovementSpeed > 0 ? st.OriginalMovementSpeed : player.Physical.WalkSpeedLimit;
                player.Physical.WalkSpeedLimit = frozen ? 0f : Mathf.Max(0.1f, baseSpd * (KeepMeAliveSettings.DOWNED_MOVEMENT_SPEED.Value / 100f));
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedMovement] ApplyDownedMovementSpeed error: {ex.Message}"); }
        }

        // Enforce prone pose and clear hands when needed (gated to avoid per-frame spam).
        public static void ApplyDownedMovementRestrictions(Player player, RMPlayer st)
        {
            try
            {
                if (player.MovementContext == null) return;

                bool shouldSetProne = false;
                if (player.MovementContext.PoseLevel > 0.01f || !player.MovementContext.IsInPronePose)
                {
                    player.MovementContext.SetPoseLevel(0f, true);
                    player.MovementContext.IsInPronePose = true;
                    shouldSetProne = true;
                }

                player.ActiveHealthController.SetStaminaCoeff(1f);

                if (shouldSetProne && st.State != RMState.Reviving && !st.IsBeingRevived && !st.IsSelfReviving)
                {
                    if (player.HandsController != null && player.HandsController.Item != null)
                    {
                        player.SetEmptyHands(null);
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedMovement] ApplyDownedMovementRestrictions error: {ex.Message}"); }
        }

        //====================[ Private Helpers ]====================
        private static IEnumerator DeferredSetEmptyHands(Player player)
        {
            yield return null;
            try { player?.SetEmptyHands(null); } catch (Exception ex) { Plugin.LogSource.LogWarning($"[DownedMovement] DeferredSetEmptyHands warn: {ex.Message}"); }
        }
    }
}
