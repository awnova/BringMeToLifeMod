using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;

namespace RevivalMod.Helpers
{
    internal static class Utils
    {
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

        public static string GetPlayerDisplayName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return playerId;
            var p = GetPlayerById(playerId);
            var nick = p?.Profile?.Nickname;
            return string.IsNullOrEmpty(nick) ? playerId : nick;
        }

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
                return FindDefibItem(player) != null;
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
                return FindDefibItem(player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GetDefib error: {ex.Message}");
                return null;
            }
        }

        public static void ConsumeDefibItem(Player player, Item defibItem)
        {
            ConsumeItem(player, defibItem, "Defibrillator");
        }

        public static void ConsumeMedicalItem(Player player, Item medicalItem)
        {
            ConsumeItem(player, medicalItem, "Medical item");
        }

        private static void ConsumeItem(Player player, Item item, string label)
        {
            try
            {
                if (player == null || item == null) return;

                Plugin.LogSource.LogInfo($"Consuming {label}: {item.LocalizedName()} (Template: {item.TemplateId})");

                var inv = player.InventoryController;
                var remove = InteractionsHandlerClass.Remove(item, inv, true);

                if (remove.Failed)
                {
                    Plugin.LogSource.LogWarning($"Remove failed: {remove.Error}, trying Discard...");
                    var discard = InteractionsHandlerClass.Discard(item, inv, true);

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
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Consume{label} error: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
