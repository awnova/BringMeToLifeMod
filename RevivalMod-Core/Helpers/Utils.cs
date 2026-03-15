//====================[ Imports ]====================
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;

namespace KeepMeAlive.Helpers
{
    //====================[ Utils ]====================
    internal static class Utils
    {
        //====================[ Network / HTTP ]====================
        
        // SPT wraps all HTTP responses as: {"err":0,"errmsg":"","data":{...}}. We must extract the inner "data" field before deserializing into T.
        private class SptEnvelope<T> 
        { 
            public T data { get; set; } 
        }

        public static T ServerRoute<T>(string url, object data = default)
        {
            try
            {
                string jsonReq = JsonConvert.SerializeObject(data);
                string jsonRes = RequestHandler.PostJson(url, jsonReq);

                // Try unwrapping the SPT envelope first.
                var envelope = JsonConvert.DeserializeObject<SptEnvelope<T>>(jsonRes);
                if (envelope != null && envelope.data != null)
                {
                    return envelope.data;
                }

                // Fallback: response was already unwrapped (plain JSON).
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

        //====================[ Player Lookups ]====================
        public static Player GetYourPlayer()
        {
            if (!Singleton<GameWorld>.Instantiated)
            {
                return null;
            }
            
            var player = Singleton<GameWorld>.Instance.MainPlayer;
            return (player != null && player.IsYourPlayer) ? player : null;
        }

        public static Player GetPlayerById(string id)
        {
            if (string.IsNullOrEmpty(id) || !Singleton<GameWorld>.Instantiated)
            {
                return null;
            }
            
            return Singleton<GameWorld>.Instance.GetEverExistedPlayerByID(id);
        }

        public static string GetPlayerDisplayName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return playerId;
            }
            
            var p = GetPlayerById(playerId);
            var nick = p?.Profile?.Nickname;
            
            return string.IsNullOrEmpty(nick) ? playerId : nick;
        }

        //====================[ Inventory / Item Utils ]====================
        private static Item FindDefibItem(Player player)
        {
            try
            {
                var items = player?.Inventory?.AllRealPlayerItems;
                if (items == null)
                {
                    return null;
                }

                string templateId = RevivalModSettings.REVIVAL_ITEM_ID.Value;
                foreach (var it in items)
                {
                    if (it?.TemplateId == templateId)
                    {
                        return it;
                    }
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

        // Shared ApplyItem helper used by both team-heal and downed revive flows.
        // Keep owner-only execution here so authority remains on the local owner client.
        public static bool TryApplyItemLikeTeamHeal(Player player, Item item, string contextLabel)
        {
            string label = string.IsNullOrEmpty(contextLabel) ? "ApplyItem" : contextLabel;

            if (player == null || !player.IsYourPlayer)
            {
                return false;
            }

            if (item is MedsItemClass meds)
            {
                if (player.HealthController == null)
                {
                    Plugin.LogSource.LogWarning($"[{label}] ApplyItem skipped: HealthController missing for {player.ProfileId}");
                    return false;
                }

                try
                {
                    player.HealthController.ApplyItem(meds, EBodyPart.Common);
                    return true;
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError($"[{label}] ApplyItem failed: {ex.Message}");
                    return false;
                }
            }

            // Non-MedsItemClass (e.g. defibrillator): remove from inventory directly.
            return TryRemoveItemFromInventory(player, item, label);
        }

        /// <summary>
        /// Removes a single item from the player's inventory grid.
        /// Used to consume non-medical revival items (e.g. defibrillators).
        /// </summary>
        private static bool TryRemoveItemFromInventory(Player player, Item item, string label)
        {
            try
            {
                if (item.CurrentAddress == null)
                {
                    Plugin.LogSource.LogWarning($"[{label}] Item {item.Id} has no CurrentAddress, cannot remove");
                    return false;
                }

                var result = item.CurrentAddress.RemoveWithoutRestrictions(item);
                if (result.Succeeded)
                {
                    item.CurrentAddress = null;
                    Plugin.LogSource.LogInfo($"[{label}] Removed item {item.Id} from inventory");
                    return true;
                }

                Plugin.LogSource.LogWarning($"[{label}] RemoveWithoutRestrictions failed for {item.Id}: {result.Error}");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[{label}] Remove item failed: {ex.Message}");
                return false;
            }
        }

    }
}