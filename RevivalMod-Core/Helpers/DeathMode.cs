//====================[ Imports ]====================
using System;
using System.Collections.Generic;
using EFT;
using EFT.HealthSystem;
using EFT.Communications;
using EFT.UI;
using KeepMeAlive.Components;
using KeepMeAlive.Features;
using UnityEngine;

namespace KeepMeAlive.Helpers
{
    //====================[ DeathMode ]====================
    // Centralized death-blocking rules used by DeathPatch.
    public static class DeathMode
    {
        //====================[ Fields ]====================
        // Throttle log spam: track last log time per player
        private static readonly Dictionary<string, float> LastLogTime = new Dictionary<string, float>();
        private const float LOG_THROTTLE_SECONDS = 5f;

        //====================[ Core Rules ]====================
        // Returns true to block death (enter/keep critical/invuln), false to allow death.
        public static bool ShouldBlockDeath(Player player, EDamageType damageType)
        {
            if (player is null || player.IsAI) return false;

            string playerId = player.ProfileId;

            if (RMSession.HasPlayerState(playerId) && RMSession.GetPlayerState(playerId).KillOverride)
            {
                Plugin.LogSource.LogDebug($"[DeathMode] KillOverride set for {playerId}, allowing death.");
                return false;
            }

            if (DownedStateController.IsPlayerInCriticalState(playerId))
            {
                if (RevivalModSettings.DEATH_BLOCK_IN_CRITICAL.Value)
                {
                    // Throttle log spam - only log once every few seconds
                    float currentTime = Time.time;
                    if (!LastLogTime.TryGetValue(playerId, out float lastTime) || 
                        currentTime - lastTime >= LOG_THROTTLE_SECONDS)
                    {
                        Plugin.LogSource.LogDebug($"[DeathMode] {playerId} critical; blocking death from {damageType}.");
                        LastLogTime[playerId] = currentTime;
                    }
                    return true;
                }
                return false;
            }

            if (DownedStateController.IsPlayerInvulnerable(playerId))
            {
                Plugin.LogSource.LogDebug($"[DeathMode] {playerId} revived/invulnerable; blocking death.");
                return true;
            }

            if (DownedStateController.IsRevivalOnCooldown(playerId))
            {
                return false;
            }

            Plugin.LogSource.LogInfo($"[DeathMode] PREVENT: {playerId} lethal {damageType} -> enter critical.");
            return true;
        }

        // Hardcore headshot rule. Returns true to allow death immediately.
        public static bool ShouldAllowDeathFromHardcoreHeadshot(ActiveHealthController healthController, EDamageType damageType)
        {
            if (!RevivalModSettings.HARDCORE_MODE.Value || !RevivalModSettings.HARDCORE_HEADSHOT_DEFAULT_DEAD.Value)
            {
                return false;
            }

            if (damageType != EDamageType.Bullet || healthController.GetBodyPartHealth(EBodyPart.Head, true).Current >= 1f)
            {
                return false;
            }

            // Config value is 0–1 (description: 0.75 = 75% survival chance).
            float roll = UnityEngine.Random.Range(0f, 1f);

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

        //====================[ Executions ]====================
        // Force the player to die now (end of critical bleed-out).
        public static void ForceBleedout(Player player)
        {
            if (player is null) return;

            try
            {
                string id = player.ProfileId;
                var st = RMSession.GetPlayerState(id);

                st.KillOverride = true;
                st.IsBeingRevived = false;
                st.IsSelfReviving = false;
                st.IsPlayingRevivalAnimation = false;
                st.State = RMState.None;
                st.FinalizedReviveCycleId = -1;

                st.CriticalStateMainTimer?.Stop(); 
                st.CriticalStateMainTimer = null;
                st.RevivePromptTimer?.Stop(); 
                st.RevivePromptTimer = null;

                VFX_UI.HideTransitPanel();
                VFX_UI.HideObjectivePanel();
                VFX_UI.Text(Color.black, "You died");

                RMSession.RemovePlayerFromCriticalPlayers(id);
                RevivalAuthority.NotifyReset(id);

                // Clear ghost flag but don't re-add to enemy lists — Kill() fires BSG's death handlers to auto-cleanup.
                GhostMode.ClearGhostFlag(id);
                GodMode.Disable(player);

                try
                {
                    MedicalAnimations.CleanupFakeItems(player);
                }
                catch (Exception ex)
                { 
                    Plugin.LogSource.LogError($"[DeathMode] CleanupFakeItems error: {ex.Message}"); 
                }

                Fika.FikaBridge.SendPlayerStateResetPacket(id, isDead: true);
                st.ResyncCooldown = -1f; // immediate resync before kill

                // Clean up log throttle tracker
                LastLogTime.Remove(id);

                EDamageType dmg = st.PlayerDamageType;
                var chest = player.ActiveHealthController.Dictionary_0[EBodyPart.Chest].Health;
                player.ActiveHealthController.Dictionary_0[EBodyPart.Chest].Health = new HealthValue(0f, chest.Maximum, 0f);

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