//====================[ Imports ]====================
using System;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using RevivalMod.Components;
using UnityEngine;

namespace RevivalMod.Helpers
{
    //====================[ PlayerRestorations ]====================
    internal static class PlayerRestorations
    {
        //====================[ Body Part Restoration ]====================
        public static void RestoreDestroyedBodyParts(Player player, bool sendNetworkPacket = true)
        {
            if (player == null) { Plugin.LogSource.LogError("RestoreDestroyedBodyParts: player is null."); return; }
            if (!RevivalModSettings.RESTORE_DESTROYED_BODY_PARTS.Value)
            {
                Plugin.LogSource.LogDebug("Body part restoration skipped (disabled in settings).");
                return;
            }

            try
            {
                var hc = player.ActiveHealthController;
                if (hc == null) { Plugin.LogSource.LogError("RestoreDestroyedBodyParts: ActiveHealthController is null."); return; }

                Plugin.LogSource.LogInfo("Restoring destroyed body parts…");

                foreach (EBodyPart part in Enum.GetValues(typeof(EBodyPart)))
                {
                    if (part == EBodyPart.Common) continue; // skip global pool

                    var state = hc.Dictionary_0[part];
                    Plugin.LogSource.LogDebug($"{part} at {hc.GetBodyPartHealth(part).Current} hp");

                    if (!state.IsDestroyed) continue;
                    RestoreOneBodyPart(hc, part, state);
                }

                Plugin.LogSource.LogInfo("Body part restoration complete.");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Body part restoration error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void RestoreOneBodyPart(
            ActiveHealthController hc,
            EBodyPart part,
            GClass2814<ActiveHealthController.GClass2813>.BodyPartState state)
        {
            try
            {
                state.IsDestroyed = false;

                float pct   = GetRestorePercentFor(part);
                float newHp = state.Health.Maximum * pct;
                state.Health = new HealthValue(newHp, state.Health.Maximum, 0f);

                hc.method_43(part, EDamageType.Medicine); // internal heal event
                hc.method_35(part);                       // refresh status
                hc.RemoveNegativeEffects(part);           // clear fracture/bleed/etc.

                FireBodyPartRestoredEvent(hc, part, state.Health.CurrentAndMaximum);

                Plugin.LogSource.LogDebug($"Restored {part} → {pct * 100f:0.#}% ({newHp}/{state.Health.Maximum}).");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Restore {part} error: {ex.Message}");
            }
        }

        private static float GetRestorePercentFor(EBodyPart part) =>
            part switch
            {
                EBodyPart.Head                   => RevivalModSettings.RESTORE_HEAD_PERCENTAGE.Value    / 100f,
                EBodyPart.Chest                  => RevivalModSettings.RESTORE_CHEST_PERCENTAGE.Value   / 100f,
                EBodyPart.Stomach                => RevivalModSettings.RESTORE_STOMACH_PERCENTAGE.Value / 100f,
                EBodyPart.LeftArm or EBodyPart.RightArm => RevivalModSettings.RESTORE_ARMS_PERCENTAGE.Value    / 100f,
                EBodyPart.LeftLeg or EBodyPart.RightLeg => RevivalModSettings.RESTORE_LEGS_PERCENTAGE.Value    / 100f,
                _ => 0.5f
            };

        private static void FireBodyPartRestoredEvent(ActiveHealthController hc, EBodyPart part, ValueStruct healthValue)
        {
            try
            {
                var field = typeof(ActiveHealthController).GetField("BodyPartRestoredEvent", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) { Plugin.LogSource.LogWarning("BodyPartRestoredEvent not found via reflection."); return; }

                if (field.GetValue(hc) is not MulticastDelegate del)
                {
                    Plugin.LogSource.LogDebug("BodyPartRestoredEvent has no subscribers.");
                    return;
                }

                foreach (var handler in del.GetInvocationList()) handler.DynamicInvoke(part, healthValue);
                Plugin.LogSource.LogDebug($"BodyPartRestoredEvent fired for {part}.");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"BodyPartRestoredEvent dispatch error: {ex.Message}");
            }
        }

        //====================[ Movement Speed Restoration ]====================
        public static void StoreOriginalMovementSpeed(Player player)
        {
            if (player is null) return;

            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);
                if (st.OriginalMovementSpeed < 0) st.OriginalMovementSpeed = player.Physical.WalkSpeedLimit;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PlayerRestorations] StoreOriginalMovementSpeed: {ex.Message}");
            }
        }

        public static void RestorePlayerMovement(Player player)
        {
            if (player is null) return;

            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);

                if (st.OriginalMovementSpeed > 0)
                    player.Physical.WalkSpeedLimit = st.OriginalMovementSpeed;

                player.MovementContext.SetPoseLevel(1f);
                player.MovementContext.EnableSprint(true);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PlayerRestorations] RestorePlayerMovement: {ex.Message}");
            }
        }

        //====================[ Awareness Restoration ]====================
        public static void SetAwarenessZero(Player player)
        {
            if (player is null) return;

            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);

                if (!st.HasStoredAwareness)
                {
                    st.OriginalAwareness  = player.Awareness;
                    st.HasStoredAwareness = true;
                }

                player.Awareness = 0f;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PlayerRestorations] SetAwarenessZero: {ex.Message}");
            }
        }

        public static void RestoreAwareness(Player player)
        {
            if (player is null) return;

            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);
                if (st.HasStoredAwareness)
                {
                    player.Awareness      = st.OriginalAwareness;
                    st.HasStoredAwareness = false;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PlayerRestorations] RestoreAwareness: {ex.Message}");
            }
        }
    }
}
