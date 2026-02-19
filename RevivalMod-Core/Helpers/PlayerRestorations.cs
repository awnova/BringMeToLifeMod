using System;
using EFT;
using EFT.HealthSystem;
using RevivalMod.Components;

namespace RevivalMod.Helpers
{
    internal static class PlayerRestorations
    {
        public static void RestoreDestroyedBodyParts(Player player)
        {
            if (player == null) return;
            if (!RevivalModSettings.RESTORE_DESTROYED_BODY_PARTS.Value) return;

            try
            {
                var hc = player.ActiveHealthController;
                if (hc == null) return;

                foreach (EBodyPart part in Enum.GetValues(typeof(EBodyPart)))
                {
                    if (part == EBodyPart.Common) continue;
                    if (!hc.IsBodyPartDestroyed(part)) continue;
                    RestoreOneBodyPart(hc, part);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Body part restoration error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void RestoreOneBodyPart(ActiveHealthController hc, EBodyPart part)
        {
            try
            {
                if (!hc.FullRestoreBodyPart(part)) return;

                var currentHealth = hc.GetBodyPartHealth(part);
                float pct = GetRestorePercentFor(part);
                float newHp = currentHealth.Maximum * pct;
                float delta = newHp - currentHealth.Current;
                if (!delta.Equals(0f))
                    hc.ChangeHealth(part, delta, default);

                hc.RemoveNegativeEffects(part);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Restore {part} error: {ex.Message}");
            }
        }

        private static float GetRestorePercentFor(EBodyPart part) =>
            part switch
            {
                EBodyPart.Head => RevivalModSettings.RESTORE_HEAD_PERCENTAGE.Value / 100f,
                EBodyPart.Chest => RevivalModSettings.RESTORE_CHEST_PERCENTAGE.Value / 100f,
                EBodyPart.Stomach => RevivalModSettings.RESTORE_STOMACH_PERCENTAGE.Value / 100f,
                EBodyPart.LeftArm or EBodyPart.RightArm => RevivalModSettings.RESTORE_ARMS_PERCENTAGE.Value / 100f,
                EBodyPart.LeftLeg or EBodyPart.RightLeg => RevivalModSettings.RESTORE_LEGS_PERCENTAGE.Value / 100f,
                _ => 0.5f
            };

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

        public static void SetAwarenessZero(Player player)
        {
            if (player is null) return;
            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);
                if (!st.HasStoredAwareness)
                {
                    st.OriginalAwareness = player.Awareness;
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
                    player.Awareness = st.OriginalAwareness;
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
