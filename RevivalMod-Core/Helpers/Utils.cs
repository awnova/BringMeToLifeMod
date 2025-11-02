//====================[ Imports ]====================
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;

namespace RevivalMod.Helpers
{
    //====================[ Utils ]====================
    internal static class Utils
    {
        //====================[ Server Routes ]====================
        public static T ServerRoute<T>(string url, object data = default)
        {
            try
            {
                string jsonReq = JsonConvert.SerializeObject(data);
                string jsonRes = RequestHandler.PostJson(url, jsonReq);
                return JsonConvert.DeserializeObject<T>(jsonRes);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"ServerRoute<T> error: {ex.Message}");
                return default;
            }
        }

        public static string ServerRoute(string url, object data = default)
        {
            try
            {
                // Allow passing raw string; wrap as { "data": "<value>" }
                string json = data is string s
                    ? JsonConvert.SerializeObject(new Dictionary<string, string> { { "data", s } })
                    : JsonConvert.SerializeObject(data);

                return RequestHandler.PutJson(url, json);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"ServerRoute error: {ex.Message}");
                return string.Empty;
            }
        }

        //====================[ Player Lookups ]====================
        public static Player GetYourPlayer()
        {
            if (!Singleton<GameWorld>.Instantiated) return null;

            var player = Singleton<GameWorld>.Instance.MainPlayer;
            return (player != null && player.IsYourPlayer) ? player : null;
        }

        public static Player GetPlayerById(string id)
        {
            if (string.IsNullOrEmpty(id) || !Singleton<GameWorld>.Instantiated) return null;
            return Singleton<GameWorld>.Instance.GetEverExistedPlayerByID(id);
        }

        public static List<Player> GetAllPlayersAndBots()
        {
            if (!Singleton<GameWorld>.Instantiated) return new List<Player>(0);
            return Singleton<GameWorld>.Instance.AllAlivePlayersList;
        }

        /// <summary>Nickname if available, else the playerId.</summary>
        public static string GetPlayerDisplayName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return playerId;

            var p = GetPlayerById(playerId);
            var nick = p?.Profile?.Nickname;
            return string.IsNullOrEmpty(nick) ? playerId : nick;
        }

        //====================[ Inventory Operations ]====================
        /// <summary>Find first configured revival item (defib) in inventory.</summary>
        private static Item FindDefibItem(Player player)
        {
            try
            {
                var items = player?.Inventory?.AllRealPlayerItems;
                if (items == null) return null;

                string templateId = RevivalModSettings.REVIVAL_ITEM_ID.Value;
                foreach (var it in items)
                {
                    if (it?.TemplateId == templateId) return it;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"FindDefibItem error: {ex.Message}");
            }
            return null;
        }

        public static bool HasDefib(Player player)
        {
            try
            {
                var item = FindDefibItem(player);
                if (item != null)
                {
                    Plugin.LogSource.LogDebug($"Found defib in inventory: {item.LocalizedName()}");
                    return true;
                }

                Plugin.LogSource.LogDebug("No defib found in player inventory");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"HasDefib error: {ex.Message}");
                return false;
            }
        }

        public static Item GetDefib(Player player)
        {
            try
            {
                var item = FindDefibItem(player);
                if (item != null)
                {
                    Plugin.LogSource.LogDebug($"Getting defib from inventory: {item.LocalizedName()}");
                    return item;
                }

                Plugin.LogSource.LogWarning("GetDefib called but no defib found in inventory");
                return null;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GetDefib error: {ex.Message}");
                return null;
            }
        }

        /// <summary>Removes and destroys the defib item from inventory.</summary>
        public static void ConsumeDefibItem(Player player, Item defibItem)
        {
            try
            {
                if (player == null)
                {
                    Plugin.LogSource.LogWarning("ConsumeDefibItem: player is null");
                    return;
                }
                if (defibItem == null)
                {
                    Plugin.LogSource.LogWarning("ConsumeDefibItem: item is null");
                    return;
                }

                Plugin.LogSource.LogInfo($"Consuming defib: {defibItem.LocalizedName()} (ID: {defibItem.Id}, Template: {defibItem.TemplateId})");

                var inv = player.InventoryController;
                GStruct454 remove = InteractionsHandlerClass.Remove(defibItem, inv, true);

                if (remove.Failed)
                {
                    Plugin.LogSource.LogWarning($"Remove failed: {remove.Error}, trying Discard...");
                    GStruct454 discard = InteractionsHandlerClass.Discard(defibItem, inv, true);

                    if (discard.Failed)
                    {
                        Plugin.LogSource.LogError($"Discard failed: {discard.Error}");
                        return;
                    }

                    inv.TryRunNetworkTransaction(discard);
                }
                else
                {
                    inv.TryRunNetworkTransaction(remove);
                }

                Plugin.LogSource.LogInfo("Defibrillator consumed successfully");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"ConsumeDefibItem error: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
