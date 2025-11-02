//====================[ Imports ]====================
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
    //====================[ DeathMode ]====================
    /// <summary>
    /// Centralized death-blocking rules used by DeathPatch.
    /// </summary>
    public static class DeathMode
    {
        //====================[ ShouldBlockDeath ]====================
        /// <summary>
        /// Decide whether the incoming kill should be blocked.
        /// Returns <b>true</b> to block death (enter/keep critical/invuln), <b>false</b> to allow death.
        /// </summary>
        public static bool ShouldBlockDeath(Player player, EDamageType damageType)
        {
            if (player is null || player.IsAI) return false;

            string playerId = player.ProfileId;

            // Explicit kill override: allow death
            if (RMSession.HasPlayerState(playerId) && RMSession.GetPlayerState(playerId).KillOverride)
            {
                Plugin.LogSource.LogDebug($"[DeathMode] KillOverride set for {playerId}, allowing death.");
                return false;
            }

            // Already critical: obey config toggle
            if (RevivalFeatures.IsPlayerInCriticalState(playerId))
            {
                if (RevivalModSettings.DEATH_BLOCK_IN_CRITICAL.Value)
                {
                    Plugin.LogSource.LogDebug($"[DeathMode] {playerId} critical; blocking death from {damageType}.");
                    return true;
                }

                Plugin.LogSource.LogDebug($"[DeathMode] {playerId} critical; blocking OFF -> allowing death from {damageType}.");
                return false;
            }

            // Post-revive invulnerability: always block (not config-dependent)
            if (RevivalFeatures.IsPlayerInvulnerable(playerId))
            {
                Plugin.LogSource.LogDebug($"[DeathMode] {playerId} revived/invulnerable; blocking death.");
                return true;
            }

            // Cooldown: allow death (prevents infinite loops)
            if (RevivalFeatures.IsRevivalOnCooldown(playerId))
            {
                Plugin.LogSource.LogDebug($"[DeathMode] {playerId} on cooldown; allowing death.");
                return false;
            }

            // First lethal hit: block and transition into critical
            Plugin.LogSource.LogInfo($"[DeathMode] PREVENT: {playerId} lethal {damageType} -> enter critical.");
            return true;
        }

        //====================[ Hardcore Headshot Rule ]====================
        /// <summary>
        /// Hardcore headshot rule. Returns <b>true</b> to allow death immediately, <b>false</b> otherwise.
        /// If random roll succeeds, death is prevented and player goes critical.
        /// </summary>
        public static bool ShouldAllowDeathFromHardcoreHeadshot(ActiveHealthController healthController, EDamageType damageType)
        {
            if (!RevivalModSettings.HARDCORE_MODE.Value ||
                !RevivalModSettings.HARDCORE_HEADSHOT_DEFAULT_DEAD.Value)
                return false;

            // Only consider bullet headshots with head < 1
            if (damageType == EDamageType.Bullet &&
                healthController.GetBodyPartHealth(EBodyPart.Head, true).Current < 1f)
            {
                float roll = UnityEngine.Random.Range(0f, 100f);

                if (roll < RevivalModSettings.HARDCORE_CHANCE_OF_CRITICAL_STATE.Value)
                {
                    Plugin.LogSource.LogInfo($"[DeathMode] Hardcore headshot spared (roll {roll:F1}). Enter critical.");
                    NotificationManagerClass.DisplayMessageNotification(
                        "Headshot – critical",
                        ENotificationDurationType.Default,
                        ENotificationIconType.Alert,
                        Color.green);
                    return false; // block death -> critical
                }

                Plugin.LogSource.LogInfo("[DeathMode] Hardcore headshot: killed instantly.");
                NotificationManagerClass.DisplayMessageNotification(
                    "Headshot – killed instantly",
                    ENotificationDurationType.Default,
                    ENotificationIconType.Alert,
                    Color.red);
                return true; // allow death
            }

            return false;
        }

        //====================[ ForceBleedout ]====================
        /// <summary>
        /// Force the player to die now (end of critical bleed-out).
        /// Sets KillOverride, clears UI/timers, exits ghost/god modes, then calls Kill().
        /// </summary>
        public static void ForceBleedout(Player player)
        {
            if (player is null) return;

            try
            {
                string id = player.ProfileId;
                var st = RMSession.GetPlayerState(id);

                // Mark and reset state
                st.KillOverride               = true;
                st.IsBeingRevived             = false;
                st.IsPlayingRevivalAnimation  = false;
                st.State                      = RMState.None; // derived flags follow

                // Clear timers
                st.CriticalStateMainTimer?.Stop(); st.CriticalStateMainTimer = null;
                st.RevivePromptTimer?.Stop();      st.RevivePromptTimer      = null;

                // UI cleanup
                VFX_UI.HideTransitPanel();
                VFX_UI.HideObjectivePanel();
                VFX_UI.Text(Color.black, "You died");

                RMSession.RemovePlayerFromCriticalPlayers(id);

                // Modes
                if (RevivalModSettings.GHOST_MODE.Value) GhostMode.ExitGhostMode(player);
                GodMode.Disable(player);

                // Execute death
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
