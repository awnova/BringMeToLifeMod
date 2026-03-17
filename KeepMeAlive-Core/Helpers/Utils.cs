//====================[ Imports ]====================
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KeepMeAlive.Helpers
{
    //====================[ Utils ]====================
    internal static class Utils
    {
        private static MethodInfo _setUseTimeMultiplierMethod;

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

                string templateId = KeepMeAliveSettings.REVIVAL_ITEM_ID.Value;
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

            if (player.HealthController == null)
            {
                Plugin.LogSource.LogWarning($"[{label}] ApplyItem skipped: HealthController missing for {player.ProfileId}");
                return false;
            }

            try
            {
                if (player.HealthController.ApplyItem(item, EBodyPart.Common))
                {
                    return true;
                }

                // Meds and food/drink should never be removed if ApplyItem fails.
                // A false return means target state could not accept the item.
                if (item is MedsItemClass || item is FoodDrinkItemClass)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[{label}] ApplyItem failed: {ex.Message}");
                if (item is MedsItemClass || item is FoodDrinkItemClass)
                {
                    return false;
                }
            }

            // Non-MedsItemClass (e.g. defibrillator): remove from inventory directly.
            return TryRemoveItemFromInventory(player, item, label);
        }

        // Keep use-speed control available for real-item ApplyItem flows.
        public static bool TrySetUseSpeed(Player player, float multiplier)
        {
            try
            {
                if (player?.HandsAnimator is not { } animator)
                {
                    return false;
                }

                _setUseTimeMultiplierMethod ??= animator.GetType().GetMethod(
                    "SetUseTimeMultiplier",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (_setUseTimeMultiplierMethod == null)
                {
                    return false;
                }

                float safeMultiplier = Mathf.Max(0.01f, multiplier);
                _setUseTimeMultiplierMethod.Invoke(animator, new object[] { safeMultiplier });
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[Utils] TrySetUseSpeed failed: {ex.Message}");
                return false;
            }
        }

        public static void ResetUseSpeed(Player player)
        {
            _ = TrySetUseSpeed(player, 1f);
        }

        public static void SetUseSpeedTemporarily(Player player, float multiplier, float resetAfterSeconds)
        {
            if (!TrySetUseSpeed(player, multiplier))
            {
                return;
            }

            if (Plugin.StaticCoroutineRunner == null || resetAfterSeconds <= 0f)
            {
                return;
            }

            Plugin.StaticCoroutineRunner.StartCoroutine(ResetUseSpeedAfterDelay(player, resetAfterSeconds));
        }

        /// <summary>
        /// Removes a single item from the player's inventory grid.
        /// Used to consume non-medical revival items (e.g. defibrillators).
        /// </summary>
        private static bool TryRemoveItemFromInventory(Player player, Item item, string label)
        {
            try
            {
                var inventoryController = player?.InventoryController;
                if (inventoryController != null)
                {
                    // Prefer EFT's controller path so Fika receives a real Remove operation.
                    // Direct address removal can desync server slot occupancy for special slots.
                    if (TryQueueControllerRemoveItem(player, item, label))
                    {
                        return true;
                    }
                }

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

        private static bool TryQueueControllerRemoveItem(Player player, Item item, string label)
        {
            try
            {
                var inventoryController = player?.InventoryController;
                if (inventoryController == null)
                {
                    return false;
                }

                var discardResult = InteractionsHandlerClass.Discard(item, (TraderControllerClass)inventoryController, true);
                if (discardResult.Failed)
                {
                    Plugin.LogSource.LogWarning($"[{label}] Controller discard failed for {item.Id}: {discardResult.Error}");
                    return false;
                }

                if (TryExecuteExplicitRemoveOperation((TraderControllerClass)inventoryController, discardResult.Value, item, label))
                {
                    return true;
                }

                inventoryController.TryRunNetworkTransaction((GStruct153)discardResult);
                Plugin.LogSource.LogInfo($"[{label}] Queued controller remove transaction fallback for {item.Id}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[{label}] Controller remove invoke failed: {ex.Message}");
            }

            return false;
        }

        private static bool TryExecuteExplicitRemoveOperation(TraderControllerClass inventoryController, GClass3408 discardResult, Item item, string label)
        {
            try
            {
                ushort operationId = GetInventoryOperationId(inventoryController);
                var operation = new RemoveOperationClass(operationId, inventoryController, discardResult);
                inventoryController.vmethod_1(operation, null);
                Plugin.LogSource.LogInfo($"[{label}] Executed explicit RemoveOperation for {item.Id} (opId={operationId})");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[{label}] Explicit RemoveOperation failed for {item.Id}: {ex.Message}");
                return false;
            }
        }

        private static ushort GetInventoryOperationId(TraderControllerClass inventoryController)
        {
            var method = inventoryController.GetType().GetMethod("method_12", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null)
            {
                throw new MissingMethodException(inventoryController.GetType().FullName, "method_12");
            }

            object value = method.Invoke(inventoryController, null);
            if (value is ushort operationId)
            {
                return operationId;
            }

            throw new InvalidOperationException("method_12 did not return ushort operation id.");
        }

        private static IEnumerator ResetUseSpeedAfterDelay(Player player, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            ResetUseSpeed(player);
        }

    }
}