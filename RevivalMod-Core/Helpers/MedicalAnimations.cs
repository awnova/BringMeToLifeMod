using System;
using System.Collections;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using KeepMeAlive.Components;
using UnityEngine;

namespace KeepMeAlive.Helpers
{
    public static class MedicalAnimations
    {
        //====================[ Constants & Caches ]====================
        private const string SURVKIT_TEMPLATE_ID = "5d02797c86f774203f38e30a", CMS_TEMPLATE_ID = "5d02778e86f774203e7dedbe";
        private const string SURVKIT_ID_PREFIX = "dea1", CMS_ID_PREFIX = "dea2";
        private const float BASE_SURVKIT_DURATION = 20f, BASE_CMS_DURATION = 17f;
        private const float SET_SPEED_DELAY = 0.2f, END_BUFFER = 0.3f;

        private static readonly SurgicalItemType[] _allTypes = { SurgicalItemType.SurvKit, SurgicalItemType.CMS };
        private static MethodInfo _setUseTimeMultiplierMethod;

        // Reflection cache for nulling MedItem on active MedEffects in NetworkHealthControllerAbstractClass
        private static FieldInfo  _nhcEffectListField; // GClass3009<T>.List_1
        private static Type       _medEffectType;      // NetworkHealthControllerAbstractClass+MedEffect
        private static PropertyInfo _nhcMedItemProp;   // MedEffect.MedItem

        //====================[ Public API ]====================

        public static Item CreateInQuestInventory(Player player, SurgicalItemType itemType)
        {
            if (!ValidatePlayer(player) || RMSession.GetPlayerState(player.ProfileId) is not { } state) return null;
            if (GetCached(state, itemType) is { } cached) return cached;

            var item = CreateAndAttach(player, itemType);
            if (item != null) SetCached(state, itemType, item);
            return item;
        }

        public static bool UseAtSpeed(Player player, SurgicalItemType itemType, float speed = 1f, Action onComplete = null)
        {
            if (!ValidatePlayer(player) || RMSession.GetPlayerState(player.ProfileId) is not { } state) return false;
            if (GetCached(state, itemType) is not { } item)
            {
                Plugin.LogSource.LogError($"[MedicalAnimations] No cached {itemType} for {player.ProfileId}. Call CreateInQuestInventory() first.");
                return false;
            }

            float baseDuration = itemType == SurgicalItemType.SurvKit ? BASE_SURVKIT_DURATION : BASE_CMS_DURATION;
            return PlayOnce(player, item, baseDuration, speed <= 0f ? 1f : speed, itemType.ToString(), onComplete);
        }

        public static bool UseWithDuration(Player player, SurgicalItemType itemType, float desiredDuration, Action onComplete = null)
        {
            if (!ValidatePlayer(player) || RMSession.GetPlayerState(player.ProfileId) is not { } state) return false;
            if (GetCached(state, itemType) is not { } item)
            {
                Plugin.LogSource.LogError($"[MedicalAnimations] No cached {itemType} for {player.ProfileId}. Call CreateInQuestInventory() first.");
                return false;
            }

            float baseDur = itemType == SurgicalItemType.SurvKit ? BASE_SURVKIT_DURATION : BASE_CMS_DURATION;
            float effectiveDur = desiredDuration - SET_SPEED_DELAY - END_BUFFER;

            if (effectiveDur <= 0f)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] Desired {desiredDuration:F2}s too short. Using default speed.");
                effectiveDur = baseDur;
            }

            return PlayOnce(player, item, baseDur, baseDur / Mathf.Max(effectiveDur, 0.001f), itemType.ToString(), onComplete);
        }

        public static void RemoveFromQuestInventory(Player player, SurgicalItemType itemType)
        {
            if (!ValidatePlayer(player) || RMSession.GetPlayerState(player.ProfileId) is not { } state) return;

            if (GetCached(state, itemType) is { } item)
            {
                // Force cancel MedEffect synchronously while Parent is valid to avoid Residue() orphan crash
                try { player.ActiveHealthController?.RemoveMedEffect(); } catch (Exception ex) { Plugin.LogSource.LogWarning($"RemoveMedEffect warn: {ex.Message}"); }
                SafeDetach(item);
            }

            SetCached(state, itemType, null);
            SetAnimationSpeed(player, 1f);
        }

        public static void CleanupAllFakeItems(Player player)
        {
            RemoveFromQuestInventory(player, SurgicalItemType.SurvKit);
            RemoveFromQuestInventory(player, SurgicalItemType.CMS);
        }

        public static void EnsureFakeItemsForRemotePlayer(Player player)
        {
            if (player == null || player.IsYourPlayer) return;
            if (RMSession.GetPlayerState(player.ProfileId) is not { } state)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] EnsureFakeItemsForRemotePlayer: No RMPlayer state for {player.ProfileId}");
                return;
            }

            foreach (var type in _allTypes)
            {
                if (GetCached(state, type) != null) continue;
                if (CreateAndAttach(player, type) is { } item)
                {
                    SetCached(state, type, item);
                    Plugin.LogSource.LogDebug($"[MedicalAnimations] Created remote fake {type} ({item.Id}) for {player.ProfileId}");
                }
                else Plugin.LogSource.LogError($"[MedicalAnimations] FAILED to create remote fake {type} for {player.ProfileId}");
            }
        }

        public static void CleanupFakeItemsForRemotePlayer(Player player)
        {
            if (player == null || player.IsYourPlayer || RMSession.GetPlayerState(player.ProfileId) is not { } state) return;

            // Null out MedItem on any active MedEffect in the ObservedHealthController BEFORE
            // detaching the item.  This prevents the NRE in MedEffect.Removed()/UpdateResource()
            // when in-flight HealthSync packets arrive after item.Parent has been cleared.
            // (ActiveHealthController is null for ObservedPlayers; we must go through HealthController.)
            foreach (var type in _allTypes)
            {
                if (GetCached(state, type) is { } item)
                {
                    NullOutNetworkMedItem(player, item);
                    SafeDetach(item);
                }
                SetCached(state, type, null);
            }
        }

        //====================[ Private: Create/Attach ]====================

        private static Item CreateAndAttach(Player player, SurgicalItemType itemType)
        {
            try
            {
                if (Singleton<ItemFactoryClass>.Instance is not { } factory) return null;

                string templateId = itemType == SurgicalItemType.SurvKit ? SURVKIT_TEMPLATE_ID : CMS_TEMPLATE_ID;
                if (factory.CreateItem(GenerateFakeItemId(player.ProfileId, itemType), templateId, null) is not Item item) return null;

                if (item is MedsItemClass meds && meds.GetItemComponent<MedKitComponent>() is { } kit)
                {
                    // BOTH SurvKit and CMS have hpResourceRate == 0 in their game templates.
                    // DoMedEffect calls TryGetBodyPartToApply → CanBeHealed which requires
                    // HpResourceRate > 0 to consider HP-damaged body parts as valid targets.
                    // Without this, DoMedEffect returns null and immediately sets FailedToApply=true,
                    // cancelling the animation right after the draw/equip phase ("first stage").
                    //
                    // MaxHpResource is overridden to 99999 so that HpResource=9999f is not clamped
                    // by the template's MaxHpResource=9. At HpResourceRate=1f/s the item holds
                    // 9999 resource — well beyond any animation duration — preventing auto-removal
                    // from depleting the resource mid-animation.
                    kit.IMedkitResource = new FakeMedkitResource(kit.IMedkitResource);
                    kit.HpResource = 9999f;
                }

                return TryAddToQuest(player, item) ? item : null;
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[MedicalAnimations] CreateAndAttach failed: {ex.Message}"); return null; }
        }

        private static bool TryAddToQuest(Player player, Item item)
        {
            try
            {
                if (player?.InventoryController?.Inventory?.QuestRaidItems?.Grids is not { } grids)
                {
                    Plugin.LogSource.LogWarning($"[MedicalAnimations] QuestRaidItems or Grids null for {player?.ProfileId}");
                    return false;
                }

                foreach (var grid in grids)
                {
                    if (grid?.FindFreeSpace(item) is { } loc && grid.AddItemWithoutRestrictions(item, loc).Succeeded)
                        return true;
                }
                Plugin.LogSource.LogWarning($"[MedicalAnimations] No grid space for item {item.Id} on {player?.ProfileId}");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[MedicalAnimations] TryAddToQuest warn: {ex.Message}"); }
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

                // Proceed triggers MedsController creation and Fika ProceedPackets for remote client sync
                player.Proceed(meds, new GStruct382<EBodyPart>(EBodyPart.Common), null, 0, false);

                if (Mathf.Abs(speed - 1f) > 0.01f) Plugin.StaticCoroutineRunner.StartCoroutine(SetSpeedLater(player, speed));

                float totalDelay = (baseDuration / Mathf.Max(speed, 0.001f)) + END_BUFFER;
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
            try { player?.SetEmptyHands(null); } catch (Exception ex) { Plugin.LogSource.LogWarning($"EndAfter cleanup warn: {ex.Message}"); }
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
                if (item?.Parent?.Container is StashGridClass grid) grid.RemoveWithoutRestrictions(item);
                else if (item?.Parent?.Container is Slot slot && ReferenceEquals(slot.ContainedItem, item)) slot.RemoveItemWithoutRestrictions();
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[MedicalAnimations] SafeDetach warn: {ex.Message}"); }
        }

        /// <summary>
        /// Before detaching a fake item from QuestRaidItems, find any active MedEffect inside the
        /// remote player's ObservedHealthController (NetworkHealthControllerAbstractClass) that still
        /// holds a reference to this item and REMOVE it from the effects list entirely.
        /// Nulling MedItem is insufficient — UpdateResource() accesses MedItem without a null-guard
        /// and would NRE anyway.  Removing the entry prevents UpdateResource() from ever being
        /// called on the stale effect when in-flight Fika HealthSync packets arrive after
        /// SafeDetach() clears item.Parent.
        /// Only meaningful for remote players; local players use ActiveHealthController.RemoveMedEffect().
        /// </summary>
        private static void NullOutNetworkMedItem(Player player, Item item)
        {
            if (player == null || item == null || player.IsYourPlayer) return;
            try
            {
                // Lazy-init reflection handles (once per session).
                if (_nhcEffectListField == null)
                {
                    // List_1 is declared on GClass3009<T> (base of NetworkHealthControllerAbstractClass).
                    var baseType = typeof(NetworkHealthControllerAbstractClass).BaseType;
                    _nhcEffectListField = baseType?.GetField("List_1",
                        BindingFlags.Public | BindingFlags.Instance);

                    _medEffectType = typeof(NetworkHealthControllerAbstractClass)
                        .GetNestedType("MedEffect", BindingFlags.NonPublic | BindingFlags.Public);

                    _nhcMedItemProp = _medEffectType?.GetProperty("MedItem",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (_nhcEffectListField == null || _medEffectType == null || _nhcMedItemProp == null)
                    {
                        Plugin.LogSource.LogWarning("[MedicalAnimations] NullOutNetworkMedItem: reflection init incomplete — skipping.");
                        return;
                    }
                }

                // ObservedHealthController IS a NetworkHealthControllerAbstractClass.
                if (player.HealthController is not NetworkHealthControllerAbstractClass nhc) return;

                var rawList = _nhcEffectListField.GetValue(nhc) as System.Collections.IList;
                if (rawList == null) return;

                // Iterate in reverse so RemoveAt doesn't shift indices we haven't visited.
                for (int i = rawList.Count - 1; i >= 0; i--)
                {
                    var effect = rawList[i];
                    if (effect == null || !_medEffectType.IsInstanceOfType(effect)) continue;
                    if (_nhcMedItemProp.GetValue(effect) is Item medItem && ReferenceEquals(medItem, item))
                    {
                        // Remove the entire entry — do NOT just null MedItem; UpdateResource() does
                        // not guard against a null MedItem and would still throw an NRE.
                        rawList.RemoveAt(i);
                        Plugin.LogSource.LogDebug($"[MedicalAnimations] Removed MedEffect [{i}] for item {item.Id} on {player.ProfileId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] NullOutNetworkMedItem warn: {ex.Message}");
            }
        }

        //====================[ Public: Identity Helpers ]====================

        /// <summary>
        /// Returns true if <paramref name="item"/> is a fake revival item created by this class
        /// (CMS or SurvKit with the well-known dea1/dea2 ID prefix).  Used by Harmony patches that
        /// suppress ObservedInventoryController events for team-heal foreign items — those patches
        /// must allow revival fake items through so the NetworkHealthControllerAbstractClass.MedEffect
        /// lifecycle works normally.
        /// </summary>
        public static bool IsFakeRevivalItem(Item item) =>
            item != null &&
            (item.Id.StartsWith(SURVKIT_ID_PREFIX, StringComparison.Ordinal) ||
             item.Id.StartsWith(CMS_ID_PREFIX, StringComparison.Ordinal));

        //====================[ Private: Utils ]====================

        private static bool ValidatePlayer(Player player) => player != null && player.IsYourPlayer;

        private static string GenerateFakeItemId(string profileId, SurgicalItemType itemType)
        {
            string suffix = profileId.Length >= 24 ? profileId.Substring(4) : profileId.PadLeft(20, '0').Substring(0, 20);
            return (itemType == SurgicalItemType.SurvKit ? SURVKIT_ID_PREFIX : CMS_ID_PREFIX) + suffix;
        }

        private static Item GetCached(RMPlayer s, SurgicalItemType t) => t == SurgicalItemType.CMS ? s.FakeCmsItem : s.FakeSurvKitItem;
        private static void SetCached(RMPlayer s, SurgicalItemType t, Item item)
        {
            if (t == SurgicalItemType.CMS) s.FakeCmsItem = item;
            else s.FakeSurvKitItem = item;
        }

        private static bool SetAnimationSpeed(Player player, float mult)
        {
            try
            {
                if (player?.HandsAnimator is not { } anim) return false;

                _setUseTimeMultiplierMethod ??= anim.GetType().GetMethod("SetUseTimeMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _setUseTimeMultiplierMethod?.Invoke(anim, new object[] { mult });
                return true;
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[MedicalAnimations] SetAnimationSpeed warn: {ex.Message}"); return false; }
        }

        //====================[ Nested Types ]====================

        // Both SurvKit and CMS ship with hpResourceRate == 0 in their game templates.
        // This wrapper forces a positive rate so DoMedEffect / CanBeHealed accepts HP-damaged
        // body parts as valid targets. Without it the animation is silently cancelled right
        // after the draw/equip phase (FailedToApply = true, no error logged by EFT).
        //
        // MaxHpResource is also inflated so that the HpResource=9999f we set on the item
        // is not clamped back to the template's MaxHpResource=9. At HpResourceRate=1f the
        // resource holds 9999 seconds worth — far longer than any animation duration.
        private sealed class FakeMedkitResource : IMedkitResource
        {
            private readonly IMedkitResource _original;
            public FakeMedkitResource(IMedkitResource original) => _original = original;
            public int MaxHpResource => 99999; // Allow HpResource=9999f without being clamped
            public float HpResourceRate => 1f;  // Force positive so CanBeHealed passes
            public EBodyPart[] BodyPartPriority => _original.BodyPartPriority;
        }

        public enum SurgicalItemType { SurvKit, CMS }
    }
}