//====================[ Imports ]====================
using System;
using System.Reflection;
using EFT;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.UI;
using HarmonyLib;
using RevivalMod.Helpers;
using SPT.Reflection.Patching;
using UnityEngine;

namespace RevivalMod.Features
{
    //====================[ RevivalFeatures ]====================
    /// <summary>
    /// Implements a second-chance mechanic for players, allowing them to enter a critical state
    /// instead of dying, and use a defibrillator to revive.
    /// </summary>
    internal class RevivalFeatures : ModulePatch
    {
        //====================[ Core Patch Implementation ]====================
        // no local state needed anymore

        protected override MethodBase GetTargetMethod() =>
            // Patch the Update method of Player to check for revival and manage states
            AccessTools.Method(typeof(Player), nameof(Player.UpdateTick));

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            try
            {
                // keep world interaction collider synced for every visible player
                DownedStateController.TickBodyInteractableColliderState(__instance);

                if (!__instance.IsYourPlayer) return;

                // dev hotkeys/debug only
                CheckTestKeybinds(__instance);

                // drive the per-frame state machines
                DownedStateController.TickInvulnerability(__instance);
                DownedStateController.TickCooldown(__instance);
                DownedStateController.TickDowned(__instance);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in RevivalFeatures patch: {ex.Message}");
            }
        }

        //====================[ Test Keybinds (DEV only) ]====================
        private static void CheckTestKeybinds(Player player)
        {
            // Only enable test keybinds when TESTING mode is active
            if (!RevivalModSettings.TESTING.Value) return;

            try
            {
                // F3 = SurvKit animation (create cached item and play at default speed)
                if (Input.GetKeyDown(KeyCode.F3))
                {
                    var it = MedicalAnimations.SurgicalItemType.SurvKit;
                    MedicalAnimations.CreateInQuestInventory(player, it);
                    MedicalAnimations.UseAtSpeed(player, it, 1f);
                }

                // F4 = CMS animation (create cached item and play at default speed)
                if (Input.GetKeyDown(KeyCode.F4))
                {
                    var it = MedicalAnimations.SurgicalItemType.CMS;
                    MedicalAnimations.CreateInQuestInventory(player, it);
                    MedicalAnimations.UseAtSpeed(player, it, 1f);
                }

                // F7 = enter ghost mode (remove local player from AI enemy lists)
                if (Input.GetKeyDown(KeyCode.F7))
                {
                    try
                    {
                        if (player != null && player.IsYourPlayer)
                        {
                            RevivalMod.Helpers.GhostMode.EnterGhostModeById(player.ProfileId);
                            Plugin.LogSource.LogInfo("GhostMode test: F7 pressed - EnterGhostMode called");
                            NotificationManagerClass.DisplayMessageNotification(
                                "GhostMode: Entered ghost mode (F7)",
                                ENotificationDurationType.Default,
                                ENotificationIconType.Default,
                                Color.cyan
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogSource.LogError($"GhostMode test F7 error: {ex.Message}");
                        NotificationManagerClass.DisplayMessageNotification(
                            $"GhostMode F7 error: {ex.Message}",
                            ENotificationDurationType.Default,
                            ENotificationIconType.Alert,
                            Color.red
                        );
                    }
                }

                // F8 = exit ghost mode (re-add local player to AI enemy lists)
                if (Input.GetKeyDown(KeyCode.F8))
                {
                    try
                    {
                        if (player != null && player.IsYourPlayer)
                        {
                            RevivalMod.Helpers.GhostMode.ExitGhostModeById(player.ProfileId);
                            Plugin.LogSource.LogInfo("GhostMode test: F8 pressed - ExitGhostMode called");
                            NotificationManagerClass.DisplayMessageNotification(
                                "GhostMode: Exited ghost mode (F8)",
                                ENotificationDurationType.Default,
                                ENotificationIconType.Default,
                                Color.cyan
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogSource.LogError($"GhostMode test F8 error: {ex.Message}");
                        NotificationManagerClass.DisplayMessageNotification(
                            $"GhostMode F8 error: {ex.Message}",
                            ENotificationDurationType.Default,
                            ENotificationIconType.Alert,
                            Color.red
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TestKeybinds] Error: {ex.Message}");
            }
        }

        //====================[ Public API Wrappers ]====================
        // (delegate to DownedStateController)

        public static bool IsPlayerInCriticalState(string playerId) =>
            DownedStateController.IsPlayerInCriticalState(playerId);

        public static bool IsPlayerInvulnerable(string playerId) =>
            DownedStateController.IsPlayerInvulnerable(playerId);

        public static bool IsRevivalOnCooldown(string playerId) =>
            DownedStateController.IsRevivalOnCooldown(playerId);

        public static void SetPlayerCriticalState(Player player, bool isCritical, EDamageType damageType) =>
            DownedStateController.SetPlayerCriticalState(player, isCritical, damageType);

        public static bool TryPerformRevivalByTeammate(string playerId) =>
            DownedStateController.StartTeammateRevive(playerId);

        public static bool PerformTeammateRevival(string targetPlayerId, Player player) =>
            DownedStateController.StartTeammateRevive(targetPlayerId, player?.ProfileId ?? "");

        // Compatibility wrapper used by Harmony patches: allow callers to request a forced death by passing either a
        // Player instance or a player id string. This mirrors the shape expected by GhostModeDeathPatch and the Plugin
        // AccessTools lookup.
        public static void ForcePlayerDeath(object targetArg)
        {
            try
            {
                Player player = null;

                if (targetArg is Player p) player = p;
                else if (targetArg is string id) player = Utils.GetPlayerById(id);

                if (player == null) return;

                DownedStateController.ForceBleedout(player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"ForcePlayerDeath wrapper error: {ex.Message}");
            }
        }
    }
}
