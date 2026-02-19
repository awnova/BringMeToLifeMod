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
    internal class RevivalFeatures : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player), nameof(Player.UpdateTick));

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            try
            {
                DownedStateController.TickBodyInteractableColliderState(__instance);
                DownedStateController.TickInvulnerability(__instance);
                DownedStateController.TickCooldown(__instance);

                if (!__instance.IsYourPlayer) return;

                if (RevivalModSettings.TESTING.Value)
                    CheckTestKeybinds(__instance);

                DownedStateController.TickDowned(__instance);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in RevivalFeatures patch: {ex.Message}");
            }
        }

        private static void CheckTestKeybinds(Player player)
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F3))
                {
                    MedicalAnimations.CreateInQuestInventory(player, MedicalAnimations.SurgicalItemType.SurvKit);
                    MedicalAnimations.UseAtSpeed(player, MedicalAnimations.SurgicalItemType.SurvKit, 1f);
                }

                if (Input.GetKeyDown(KeyCode.F4))
                {
                    MedicalAnimations.CreateInQuestInventory(player, MedicalAnimations.SurgicalItemType.CMS);
                    MedicalAnimations.UseAtSpeed(player, MedicalAnimations.SurgicalItemType.CMS, 1f);
                }

                if (Input.GetKeyDown(KeyCode.F7))
                {
                    GhostMode.EnterGhostModeById(player.ProfileId);
                    NotificationManagerClass.DisplayMessageNotification(
                        "GhostMode: Entered (F7)", ENotificationDurationType.Default, ENotificationIconType.Default, Color.cyan);
                }

                if (Input.GetKeyDown(KeyCode.F8))
                {
                    GhostMode.ExitGhostModeById(player.ProfileId);
                    NotificationManagerClass.DisplayMessageNotification(
                        "GhostMode: Exited (F8)", ENotificationDurationType.Default, ENotificationIconType.Default, Color.cyan);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TestKeybinds] Error: {ex.Message}");
            }
        }

        // Public API wrappers (delegate to DownedStateController)
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

        /// <summary>
        /// Compatibility wrapper used by GhostModeDeathPatch. Accepts Player or string playerId.
        /// </summary>
        public static void ForcePlayerDeath(object targetArg)
        {
            try
            {
                Player player = targetArg switch
                {
                    Player p => p,
                    string id => Utils.GetPlayerById(id),
                    _ => null
                };
                if (player != null)
                    DownedStateController.ForceBleedout(player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"ForcePlayerDeath wrapper error: {ex.Message}");
            }
        }
    }
}
