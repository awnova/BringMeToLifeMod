using System;
using EFT;
using EFT.HealthSystem;
using EFT.Communications;
using EFT.UI;
using RevivalMod.Components;
using RevivalMod.Features;
using UnityEngine;

namespace RevivalMod.Helpers
{
    /// <summary>
    /// Centralized death-blocking rules used by DeathPatch.
    /// </summary>
    public static class DeathMode
    {
        /// <summary>
        /// Returns true to block death (enter/keep critical/invuln), false to allow death.
        /// </summary>
        public static bool ShouldBlockDeath(Player player, EDamageType damageType)
        {
            if (player is null || player.IsAI) return false;

            string playerId = player.ProfileId;

            if (RMSession.HasPlayerState(playerId) && RMSession.GetPlayerState(playerId).KillOverride)
            {
                Plugin.LogSource.LogDebug($"[DeathMode] KillOverride set for {playerId}, allowing death.");
                return false;
            }

            if (RevivalFeatures.IsPlayerInCriticalState(playerId))
            {
                if (RevivalModSettings.DEATH_BLOCK_IN_CRITICAL.Value)
                {
                    Plugin.LogSource.LogDebug($"[DeathMode] {playerId} critical; blocking death from {damageType}.");
                    return true;
                }
                return false;
            }

            if (RevivalFeatures.IsPlayerInvulnerable(playerId))
            {
                Plugin.LogSource.LogDebug($"[DeathMode] {playerId} revived/invulnerable; blocking death.");
                return true;
            }

            if (RevivalFeatures.IsRevivalOnCooldown(playerId))
                return false;

            Plugin.LogSource.LogInfo($"[DeathMode] PREVENT: {playerId} lethal {damageType} -> enter critical.");
            return true;
        }

        /// <summary>
        /// Hardcore headshot rule. Returns true to allow death immediately.
        /// </summary>
        public static bool ShouldAllowDeathFromHardcoreHeadshot(ActiveHealthController healthController, EDamageType damageType)
        {
            if (!RevivalModSettings.HARDCORE_MODE.Value ||
                !RevivalModSettings.HARDCORE_HEADSHOT_DEFAULT_DEAD.Value)
                return false;

            if (damageType != EDamageType.Bullet ||
                healthController.GetBodyPartHealth(EBodyPart.Head, true).Current >= 1f)
                return false;

            float roll = UnityEngine.Random.Range(0f, 100f);

            if (roll < RevivalModSettings.HARDCORE_CHANCE_OF_CRITICAL_STATE.Value)
            {
                Plugin.LogSource.LogInfo($"[DeathMode] Hardcore headshot spared (roll {roll:F1}). Enter critical.");
                NotificationManagerClass.DisplayMessageNotification(
                    "Headshot – critical",
                    ENotificationDurationType.Default, ENotificationIconType.Alert, Color.green);
                return false;
            }

            Plugin.LogSource.LogInfo("[DeathMode] Hardcore headshot: killed instantly.");
            NotificationManagerClass.DisplayMessageNotification(
                "Headshot – killed instantly",
                ENotificationDurationType.Default, ENotificationIconType.Alert, Color.red);
            return true;
        }

        /// <summary>
        /// Force the player to die now (end of critical bleed-out).
        /// </summary>
        public static void ForceBleedout(Player player)
        {
            if (player is null) return;

            try
            {
                string id = player.ProfileId;
                var st = RMSession.GetPlayerState(id);

                st.KillOverride = true;
                st.IsBeingRevived = false;
                st.IsPlayingRevivalAnimation = false;
                st.State = RMState.None;

                st.CriticalStateMainTimer?.Stop(); st.CriticalStateMainTimer = null;
                st.RevivePromptTimer?.Stop(); st.RevivePromptTimer = null;

                VFX_UI.HideTransitPanel();
                VFX_UI.HideObjectivePanel();
                VFX_UI.Text(Color.black, "You died");

                RMSession.RemovePlayerFromCriticalPlayers(id);
                RevivalAuthority.NotifyReset(id);

                if (RevivalModSettings.GHOST_MODE.Value) GhostMode.ExitGhostMode(player);
                GodMode.Disable(player);

                Fika.FikaBridge.SendPlayerStateResetPacket(id, isDead: true);

                EDamageType dmg = st.PlayerDamageType;
                var chest = player.ActiveHealthController.Dictionary_0[EBodyPart.Chest].Health;
                player.ActiveHealthController.Dictionary_0[EBodyPart.Chest].Health =
                    new HealthValue(0f, chest.Maximum, 0f);

                player.ActiveHealthController.Kill(dmg);

                Plugin.LogSource.LogInfo($"[DeathMode] {id} has died (forced bleedout).");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[DeathMode] ForceBleedout error: {ex.Message}");
            }
        }
    }
}
