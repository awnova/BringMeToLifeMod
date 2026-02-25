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

        /// <summary>
        /// Removes an item from the player's inventory using direct grid/slot
        /// removal — the same approach MedicalAnimations.SafeDetach uses.
        ///
        /// We intentionally avoid InteractionsHandlerClass.Remove + TryRunNetworkTransaction
        /// because TryRunNetworkTransaction is async, and calling it fire-and-forget
        /// acquires the InventoryController's transaction lock synchronously. When
        /// consumption happens on the same frame as MedicalAnimations.PlayOnce →
        /// ApplyItem (which also needs the transaction lock), the unawaited lock
        /// blocks the medical animation from starting.
        ///
        /// Direct grid removal has no transaction involvement, so ApplyItem can
        /// proceed on the same frame without contention. The end-of-raid save
        /// will reflect the updated inventory.
        /// </summary>
        private static void ConsumeItem(Player player, Item item, string label)
        {
            try
            {
                if (player == null || item == null) return;

                Plugin.LogSource.LogInfo($"Consuming {label}: {item.LocalizedName()} (Template: {item.TemplateId})");

                var parent = item.Parent;
                if (parent == null)
                {
                    Plugin.LogSource.LogWarning($"{label} item has no parent — already detached?");
                    return;
                }

                var container = parent.Container;
                if (container is StashGridClass grid)
                {
                    grid.RemoveWithoutRestrictions(item);
                    Plugin.LogSource.LogInfo($"{label} consumed (removed from grid)");
                }
                else if (container is Slot slot && ReferenceEquals(slot.ContainedItem, item))
                {
                    slot.RemoveItemWithoutRestrictions();
                    Plugin.LogSource.LogInfo($"{label} consumed (removed from slot)");
                }
                else
                {
                    // Fallback: use InteractionsHandlerClass but skip TryRunNetworkTransaction
                    // to avoid transaction-lock contention with ApplyItem on the same frame.
                    var remove = InteractionsHandlerClass.Remove(item, player.InventoryController, false);
                    if (remove.Succeeded)
                        Plugin.LogSource.LogInfo($"{label} consumed (Remove fallback)");
                    else
                        Plugin.LogSource.LogError($"{label} Remove fallback failed: {remove.Error}");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Consume{label} error: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
