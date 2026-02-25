//====================[ Imports ]====================
using System;
using System.Collections;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using RevivalMod.Components;
using UnityEngine;

//====================[ MedicalAnimations ]====================
namespace RevivalMod.Helpers
{
    //====================[ MedicalAnimations ]====================
    public static class MedicalAnimations
    {
        //====================[ Constants ]====================
        private const string SURVKIT_TEMPLATE_ID = "5d02797c86f774203f38e30a";
        private const string CMS_TEMPLATE_ID     = "5d02778e86f774203e7dedbe";

        // Type prefixes for per-player fake item IDs (4-char hex prefix + 20-char ProfileId suffix = 24-char MongoID)
        private const string SURVKIT_ID_PREFIX = "dea1";
        private const string CMS_ID_PREFIX     = "dea2";

        // Base vanilla animation lengths used to compute speed for UseAtSpeed(...)
        private const float BASE_SURVKIT_DURATION = 20f; // SurvKit ~20s
        private const float BASE_CMS_DURATION     = 17f; // CMS    ~17s

        // Timing helpers
        private const float SET_SPEED_DELAY = 0.2f; // wait before applying speed multiplier
        private const float END_BUFFER      = 0.3f; // buffer before cleanup

        //====================[ Public API ]====================

        // Create the fake item in QuestRaidItems and cache it on RMPlayer.
        public static Item CreateInQuestInventory(Player player, SurgicalItemType itemType)
        {
            if (!ValidatePlayer(player)) return null;
            var state = RMSession.GetPlayerState(player.ProfileId);
            if (state == null) return null;

            var cached = GetCached(state, itemType);
            if (cached != null) return cached;

            var item = CreateAndAttach(player, itemType);
            if (item == null) return null;

            SetCached(state, itemType, item);
            return item;
        }

        // Trigger the use animation for the cached item at a given speed (1 = default). Does not delete the item.
        public static bool UseAtSpeed(Player player, SurgicalItemType itemType, float speed = 1f, Action onComplete = null)
        {
            if (!ValidatePlayer(player)) return false;
            var state = RMSession.GetPlayerState(player.ProfileId);
            if (state == null) return false;

            var item = GetCached(state, itemType);
            if (item == null)
            {
                Plugin.LogSource.LogError($"[MedicalAnimations] No cached {itemType} for {player.ProfileId}. Call CreateInQuestInventory() first.");
                return false;
            }

            if (speed <= 0f) speed = 1f;

            float baseDuration = itemType == SurgicalItemType.SurvKit ? BASE_SURVKIT_DURATION : BASE_CMS_DURATION;
            return PlayOnce(player, item, baseDuration, speed, itemType.ToString(), onComplete);
        }

        // Play animation with a desired duration (calculates speed automatically).
        // Subtracts SET_SPEED_DELAY and END_BUFFER so the animation fits the requested window.
        public static bool UseWithDuration(Player player, SurgicalItemType itemType, float desiredDuration, Action onComplete = null)
        {
            if (!ValidatePlayer(player)) return false;
            var state = RMSession.GetPlayerState(player.ProfileId);
            if (state == null) return false;

            var item = GetCached(state, itemType);
            if (item == null)
            {
                Plugin.LogSource.LogError($"[MedicalAnimations] No cached {itemType} for {player.ProfileId}. Call CreateInQuestInventory() first.");
                return false;
            }

            float baseDuration = itemType == SurgicalItemType.SurvKit ? BASE_SURVKIT_DURATION : BASE_CMS_DURATION;

            float effectiveDuration = desiredDuration - SET_SPEED_DELAY - END_BUFFER;
            if (effectiveDuration <= 0f)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] Desired {desiredDuration:F2}s too short (needs ≥ {(SET_SPEED_DELAY + END_BUFFER):F2}s). Using default speed.");
                effectiveDuration = baseDuration; // fallback
            }

            float speed = baseDuration / Mathf.Max(effectiveDuration, 0.001f);
            return PlayOnce(player, item, baseDuration, speed, itemType.ToString(), onComplete);
        }

        // Delete/remove the fake item from QuestRaidItems and clear cache.
        public static void RemoveFromQuestInventory(Player player, SurgicalItemType itemType)
        {
            if (!ValidatePlayer(player)) return;
            var state = RMSession.GetPlayerState(player.ProfileId);
            if (state == null) return;

            var item = GetCached(state, itemType);
            if (item != null)
            {
                // Cancel the active MedEffect BEFORE detaching the item from inventory.
                // MedEffect.Residue() unconditionally calls Item.Parent.GetOwner() at the
                // end of both the normal-completion and interrupted code paths. If the item
                // is detached first, the next health-controller tick calls Residue() on an
                // orphaned item (no Parent) and throws "Trying to get parent of an item
                // that doesn't have a parent". Calling RemoveMedEffect() here drives
                // ForceResidue() → method_0() → Residue() synchronously while Parent is
                // still valid, then removes the effect from the controller's list.
                try { player.ActiveHealthController?.RemoveMedEffect(); }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"[MedicalAnimations] RemoveMedEffect warn: {ex.Message}"); }

                SafeDetach(item);
            }

            SetCached(state, itemType, null);
            SetAnimationSpeed(player, 1f); // safety reset
        }

        /// <summary>
        /// Removes both fake surgical items (SurvKit + CMS) from QuestRaidItems.
        /// Must be called at every exit point from downed state to prevent fake items
        /// from being included in the end-of-raid inventory save and causing server
        /// deserialization errors on /client/match/local/end.
        /// </summary>
        public static void CleanupAllFakeItems(Player player)
        {
            RemoveFromQuestInventory(player, SurgicalItemType.SurvKit);
            RemoveFromQuestInventory(player, SurgicalItemType.CMS);
        }

        /// <summary>
        /// Creates fake surgical items in QuestRaidItems for a remote (non-local) player.
        /// Required so Fika health-sync packets can resolve the item ID on all clients.
        /// </summary>
        public static void EnsureFakeItemsForRemotePlayer(Player player)
        {
            if (player == null || player.IsYourPlayer) return;
            var state = RMSession.GetPlayerState(player.ProfileId);
            if (state == null) return;

            foreach (var itemType in new[] { SurgicalItemType.CMS, SurgicalItemType.SurvKit })
            {
                if (GetCached(state, itemType) != null) continue;
                var item = CreateAndAttach(player, itemType);
                if (item != null) SetCached(state, itemType, item);
            }
        }

        /// <summary>
        /// Removes fake surgical items from QuestRaidItems for a remote (non-local) player.
        /// </summary>
        public static void CleanupFakeItemsForRemotePlayer(Player player)
        {
            if (player == null || player.IsYourPlayer) return;
            var state = RMSession.GetPlayerState(player.ProfileId);
            if (state == null) return;

            foreach (var itemType in new[] { SurgicalItemType.SurvKit, SurgicalItemType.CMS })
            {
                var item = GetCached(state, itemType);
                if (item != null) SafeDetach(item);
                SetCached(state, itemType, null);
            }
        }

        //====================[ Private: Create/Attach ]====================

        private static Item CreateAndAttach(Player player, SurgicalItemType itemType)
        {
            try
            {
                var factory = Singleton<ItemFactoryClass>.Instance;
                if (factory == null) return null;

                string templateId = itemType == SurgicalItemType.SurvKit ? SURVKIT_TEMPLATE_ID : CMS_TEMPLATE_ID;
                string itemId     = GenerateFakeItemId(player.ProfileId, itemType);

                var item = factory.CreateItem(itemId, templateId, null) as Item;
                if (item == null) return null;

                if (item is MedsItemClass meds)
                {
                    var kit = meds.GetItemComponent<MedKitComponent>();
                    if (kit != null) kit.HpResource = 1f; // single use
                }

                return TryAddToQuest(player, item) ? item : null;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[MedicalAnimations] CreateAndAttach failed: {ex.Message}");
                return null;
            }
        }

        private static bool TryAddToQuest(Player player, Item item)
        {
            try
            {
                var quest = player?.InventoryController?.Inventory?.QuestRaidItems;
                if (quest?.Grids == null) return false;

                foreach (var grid in quest.Grids)
                {
                    if (grid == null) continue;
                    var loc = grid.FindFreeSpace(item);
                    if (loc == null) continue;

                    var r = grid.AddItemWithoutRestrictions(item, loc);
                    if (r.Succeeded) return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] TryAddToQuest warn: {ex.Message}");
            }
            return false;
        }

        //====================[ Private: Use/Playback ]====================

        private static bool PlayOnce(Player player, Item item, float baseDuration, float speed, string label, Action onComplete)
        {
            try
            {
                if (item is not MedsItemClass meds)
                {
                    Plugin.LogSource.LogWarning($"[MedicalAnimations] {label} item not MedsItemClass.");
                    return false;
                }

                player.HealthController.ApplyItem(meds, EBodyPart.Common);

                if (Mathf.Abs(speed - 1f) > 0.01f)
                    Plugin.StaticCoroutineRunner.StartCoroutine(SetSpeedLater(player, speed));

                // actual animation time at chosen speed (+ buffer for cleanup)
                float actual = baseDuration / Mathf.Max(speed, 0.001f);
                float totalDelay = actual + END_BUFFER;

                Plugin.StaticCoroutineRunner.StartCoroutine(EndAfter(player, totalDelay, onComplete));
                return true;
            }
            catch (Exception ex)
            {
                SetAnimationSpeed(player, 1f);
                Plugin.LogSource.LogError($"[MedicalAnimations] PlayOnce failed: {ex.Message}");
                return false;
            }
        }

        private static IEnumerator SetSpeedLater(Player player, float speed)
        {
            yield return new WaitForSeconds(SET_SPEED_DELAY);
            SetAnimationSpeed(player, speed);
        }

        private static IEnumerator EndAfter(Player player, float delay, Action done)
        {
            yield return new WaitForSeconds(delay);
            try
            {
                var ctrl = player?.HandsController;
                string name = ctrl?.GetType().Name ?? string.Empty;
                if (name.IndexOf("QuickUseItem", StringComparison.OrdinalIgnoreCase) >= 0)
                    player?.SetEmptyHands(null);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] EndAfter cleanup warn: {ex.Message}");
            }
            finally
            {
                SetAnimationSpeed(player, 1f);
                done?.Invoke();
            }
        }

        //====================[ Private: Cleanup/Detach ]====================

        private static void SafeDetach(Item item)
        {
            try
            {
                var container = item?.Parent?.Container;
                if (container is StashGridClass grid) grid.RemoveWithoutRestrictions(item);
                else if (container is Slot slot && ReferenceEquals(slot.ContainedItem, item)) slot.RemoveItemWithoutRestrictions();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] SafeDetach warn: {ex.Message}");
            }
        }

        //====================[ Private: Utils ]====================

        private static bool ValidatePlayer(Player player) => player != null && player.IsYourPlayer;

        /// <summary>
        /// Generates a deterministic, per-player unique fake item ID.
        /// Format: 4-char type prefix + last 20 chars of ProfileId = valid 24-char MongoID hex string.
        /// Because this is deterministic, all Fika clients independently derive the same ID for the
        /// same player, so health-sync packets resolve to the correct item without extra coordination.
        /// </summary>
        private static string GenerateFakeItemId(string profileId, SurgicalItemType itemType)
        {
            string prefix = itemType == SurgicalItemType.SurvKit ? SURVKIT_ID_PREFIX : CMS_ID_PREFIX;
            // ProfileId is a 24-char hex string; take the last 20 chars as a player-unique suffix.
            string suffix = profileId.Length >= 24
                ? profileId.Substring(4)
                : profileId.PadLeft(20, '0').Substring(0, 20);
            return prefix + suffix;
        }

        private static Item GetCached(RMPlayer s, SurgicalItemType t) =>
            t == SurgicalItemType.CMS ? s.FakeCmsItem : s.FakeSurvKitItem;

        private static void SetCached(RMPlayer s, SurgicalItemType t, Item item)
        {
            if (t == SurgicalItemType.CMS) s.FakeCmsItem = item;
            else                           s.FakeSurvKitItem = item;
        }

        private static bool SetAnimationSpeed(Player player, float mult)
        {
            try
            {
                var anim = player?.HandsAnimator;
                if (anim == null) return false;

                var mi = anim.GetType().GetMethod("SetUseTimeMultiplier",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) return false;

                mi.Invoke(anim, new object[] { mult });
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] SetAnimationSpeed warn: {ex.Message}");
                return false;
            }
        }

        //====================[ Enums ]====================
        public enum SurgicalItemType { SurvKit, CMS }
    }
}
