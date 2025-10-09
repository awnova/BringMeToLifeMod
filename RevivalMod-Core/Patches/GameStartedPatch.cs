using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using Comfort.Common;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Linq;
using UnityEngine;
using RevivalMod.Helpers;
using RevivalMod.Features;

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
                Player playerClient = Singleton<GameWorld>.Instance.MainPlayer;

                if (playerClient == null)
                {
                    Plugin.LogSource.LogError("MainPlayer is null");
                    return;
                }

                // Check if player has revival item
                string playerId = playerClient.ProfileId;
                var inRaidItems = playerClient.Inventory.GetPlayerItems(EPlayerItems.Equipment);
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

                // Clears the override kill list
                RevivalFeatures.KillOverridePlayers.Clear();

                // Clears the Critical State list
                RevivalFeatures.ClearPlayerInCriticalState();

                // Clears the Player Critical State Timer list
                RevivalFeatures.ClearPlayerCritcalStateTimer();

                // Display notification about revival item status
                if (RevivalModSettings.TESTING.Value)
                {
                    NotificationManagerClass.DisplayMessageNotification(
                    $"Revival System: {(hasItem ? "Revival item found" : "No revival item found")}",
                    ENotificationDurationType.Default,
                    ENotificationIconType.Default,
                    hasItem ? Color.green : Color.yellow);
                }

                // Enable interactables
                Plugin.LogSource.LogDebug("Enabling body interactables");
                foreach (GameObject interact in Resources.FindObjectsOfTypeAll<GameObject>()
                        .Where(obj => obj.name.Contains("Body Interactable")))
                {
                    Plugin.LogSource.LogDebug($"Found interactable: {interact.name}");
                    interact.layer = LayerMask.NameToLayer("Interactive");
                    interact.GetComponent<BoxCollider>().enabled = true;
                }
                Utils.MainPlayer = playerClient;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in GameStartedPatch: {ex.Message}");
            }
        }
    }
}