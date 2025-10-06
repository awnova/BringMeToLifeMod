using Comfort.Common;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using System.Linq;
using RevivalMod.Helpers;

namespace RevivalMod.Patches
{
    internal class GameStartedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted));
        }

        [PatchPostfix]
        static void PatchPostfix()
        {
            try
            {
                Plugin.LogSource.LogInfo("Game started, checking revival item");

                // Make sure GameWorld is instantiated
                if (!Singleton<GameWorld>.Instantiated)
                {
                    Plugin.LogSource.LogError("GameWorld not instantiated yet");
                    return;
                }

                // Initialize player client directly
                Player player = Singleton<GameWorld>.Instance.MainPlayer;
                if (player == null)
                {
                    Plugin.LogSource.LogError("MainPlayer is null");
                    return;
                }

                // Check if player has revival item
                string playerId = player.ProfileId;
                var inRaidItems = player.Inventory.GetPlayerItems(EPlayerItems.Equipment);
                bool hasItem = false;

                try
                {
                    hasItem = inRaidItems.Any(item => item.TemplateId == Constants.Constants.ITEM_ID);
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError($"Error checking player items: {ex.Message}");
                }

                Plugin.LogSource.LogInfo($"Player {playerId} has revival item: {hasItem}");

                // Display notification about revival item status
                if (RevivalModSettings.TESTING.Value)
                {
                    NotificationManagerClass.DisplayMessageNotification(
                    $"Revival System: {(hasItem ? "Revival item found" : "No revival item found")}",
                    ENotificationDurationType.Default,
                    ENotificationIconType.Default,
                    hasItem ? Color.green : Color.yellow);
                }

            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in GameStartedPatch: {ex.Message}");
            }
        }

    }
}