using Newtonsoft.Json;
using Comfort.Common;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;

namespace RevivalMod.Helpers
{
    internal class Utils
    {
        #region Server Routes

        public static T ServerRoute<T>(string url, object data = default)
        {
            string json = JsonConvert.SerializeObject(data);
            var req = RequestHandler.PostJson(url, json);
            return JsonConvert.DeserializeObject<T>(req);
        }
        public static string ServerRoute(string url, object data = default)
        {
            string json;
            if (data is string v)
            {
                Dictionary<string, string> dataDict = new()
                {
                    { "data", v }
                };
                json = JsonConvert.SerializeObject(dataDict);
            }
            else
            {
                json = JsonConvert.SerializeObject(data);
            }

            return RequestHandler.PutJson(url, json);
        }

        #endregion

        #region Player Lookups

        public static Player GetYourPlayer()
        {
            Player player = Singleton<GameWorld>.Instance.MainPlayer;

            if (player == null || !player.IsYourPlayer)
                return null;

            return player;
        }

        public static Player GetPlayerById(string id)
        {
            Player player = Singleton<GameWorld>.Instance.GetEverExistedPlayerByID(id);
            if (player == null) return null;
            return player;
        }

        public static List<Player> GetAllPlayersAndBots()
        {
            return Singleton<GameWorld>.Instance.AllAlivePlayersList;
        }

        #endregion

        #region Inventory Operations

        /// <summary>
        /// Checks if the player has a defibrillator (or configured revival item) anywhere in their inventory
        /// </summary>
        public static bool HasDefib(Player player)
        {
            try
            {
                foreach (var item in player.Inventory.AllRealPlayerItems)
                {
                    if (item.TemplateId == RevivalModSettings.REVIVAL_ITEM_ID.Value)
                    {
                        Plugin.LogSource.LogDebug($"Found defib in inventory: {item.LocalizedName()}");
                        return true;
                    }
                }

                Plugin.LogSource.LogDebug("No defib found in player inventory");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error searching for defib: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Gets the first defibrillator (or configured revival item) from the player's inventory
        /// </summary>
        public static Item GetDefib(Player player)
        {
            try
            {
                foreach (var item in player.Inventory.AllRealPlayerItems)
                {
                    if (item.TemplateId == RevivalModSettings.REVIVAL_ITEM_ID.Value)
                    {
                        Plugin.LogSource.LogDebug($"Getting defib from inventory: {item.LocalizedName()}");
                        return item;
                    }
                }
                
                Plugin.LogSource.LogWarning("GetDefib called but no defib found in inventory");
                return null;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error getting defib: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Consumes a defibrillator item from the player's inventory (removes and destroys it)
        /// </summary>
        public static void ConsumeDefibItem(Player player, Item defibItem)
        {
            try
            {
                if (defibItem == null)
                {
                    Plugin.LogSource.LogWarning("Cannot consume defib - item is null");
                    return;
                }

                Plugin.LogSource.LogInfo($"Attempting to consume defib item: {defibItem.LocalizedName()} (ID: {defibItem.Id}, Template: {defibItem.TemplateId})");
                
                InventoryController inventoryController = player.InventoryController;
                
                // Use Remove operation to destroy the item completely
                GStruct454 removeResult = InteractionsHandlerClass.Remove(defibItem, inventoryController, true);

                if (removeResult.Failed)
                {
                    Plugin.LogSource.LogWarning($"Remove failed: {removeResult.Error}, trying Discard...");
                    
                    // Fallback to Discard (deletes the item)
                    GStruct454 discardResult = InteractionsHandlerClass.Discard(defibItem, inventoryController, true);
                    
                    if (discardResult.Failed)
                    {
                        Plugin.LogSource.LogError($"Both Remove and Discard failed: {discardResult.Error}");
                        return;
                    }
                    
                    inventoryController.TryRunNetworkTransaction(discardResult);
                }
                else
                {
                    inventoryController.TryRunNetworkTransaction(removeResult);
                }
                
                Plugin.LogSource.LogInfo("Defibrillator consumed successfully (removed and destroyed)");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error consuming defib item: {ex.Message}\n{ex.StackTrace}");
            }
        }

        #endregion
    }
}
