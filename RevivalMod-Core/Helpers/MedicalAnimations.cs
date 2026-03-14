using System;
using System.Collections;
using System.Linq;
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
        private const float SET_SPEED_DELAY = 0.5f, EARLY_FINISH_BUFFER = 0.5f;

        private static MethodInfo _setUseTimeMultiplierMethod;

        //====================[ Public API ]====================

        public static Item CreateInQuestInventory(Player player, SurgicalItemType itemType)
        {
            if (!ValidatePlayer(player) || RMSession.GetPlayerState(player.ProfileId) is not { } state) return null;
            if (GetCached(state, itemType) is { } cached) return cached;

            var item = CreateAndAttach(player, itemType);
            if (item != null) SetCached(state, itemType, item);
            return item;
        }

        // Ensures fake CMS/SURV items exist for observed players so ProceedPacket item lookups
        // can resolve on peers that do not own the revive target.
        public static void EnsureFakeItemsForRemotePlayer(Player player)
        {
            try
            {
                if (player == null || player.IsYourPlayer) return;
                if (RMSession.GetPlayerState(player.ProfileId) is not { } state) return;

                if (GetCached(state, SurgicalItemType.CMS) == null)
                {
                    var cms = CreateAndAttach(player, SurgicalItemType.CMS);
                    if (cms != null) SetCached(state, SurgicalItemType.CMS, cms);
                }

                if (GetCached(state, SurgicalItemType.SurvKit) == null)
                {
                    var surv = CreateAndAttach(player, SurgicalItemType.SurvKit);
                    if (surv != null) SetCached(state, SurgicalItemType.SurvKit, surv);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] EnsureFakeItemsForRemotePlayer warn: {ex.Message}");
            }
        }

        public static bool UseAtSpeed(Player player, SurgicalItemType itemType, float speed = 1f, Action onComplete = null)
        {
            if (!TryGetOrCreateCachedItem(player, itemType, out var item))
            {
                return false;
            }

            float baseDuration = itemType == SurgicalItemType.SurvKit ? BASE_SURVKIT_DURATION : BASE_CMS_DURATION;
            return TryApplyInternal(player, item, baseDuration, speed <= 0f ? 1f : speed, itemType.ToString(), onComplete);
        }

        public static bool TryApplyWithDuration(Player player, SurgicalItemType itemType, float desiredDuration, Action onComplete = null)
        {
            ReviveDebug.Log("MedAnim_TryApplyWithDuration_Enter", player?.ProfileId ?? "<null>", player?.IsYourPlayer ?? false, $"itemType={itemType} desiredDuration={desiredDuration:F2}");
            if (!TryGetOrCreateCachedItem(player, itemType, out var item))
            {
                return false;
            }

            float baseDur = itemType == SurgicalItemType.SurvKit ? BASE_SURVKIT_DURATION : BASE_CMS_DURATION;
            
            // Subtract SET_SPEED_DELAY (time before speedup applies) and EARLY_FINISH_BUFFER (time to finish early)
            float effectiveDur = desiredDuration - SET_SPEED_DELAY - EARLY_FINISH_BUFFER;

            if (effectiveDur <= 0f)
            {
                Plugin.LogSource.LogWarning($"[MedicalAnimations] Desired {desiredDuration:F2}s too short for {itemType}. Using default speed.");
                effectiveDur = baseDur;
            }

            return TryApplyInternal(player, item, baseDur, baseDur / Mathf.Max(effectiveDur, 0.001f), itemType.ToString(), onComplete);
        }

        public static bool UseWithDuration(Player player, SurgicalItemType itemType, float desiredDuration, Action onComplete = null)
        {
            return TryApplyWithDuration(player, itemType, desiredDuration, onComplete);
        }

public static void CleanupFakeItems(Player player, SurgicalItemType? consumedItem = null)
        {
            if (player == null || RMSession.GetPlayerState(player.ProfileId) is not { } state) return;

            // Target the item that was naturally consumed by the revival and let Tarkov's ActiveHealthController 
            // naturally remove it from the grid (otherwise MedEffect.Residue crashes on orphaned items).
            // If consumedItem == null (e.g., revive cancelled or bled out), we explicitly cancel and delete BOTH.
            if (player.IsYourPlayer && consumedItem == null)
            {
                try { player.HealthController?.CancelApplyingItem(); }
                catch (Exception ex) { Plugin.LogSource.LogError($"[MedicalAnimations] CancelApplyingItem ex: {ex}"); }
            }

            try
            {
                if (consumedItem != SurgicalItemType.SurvKit)
                {
                    if (GetCached(state, SurgicalItemType.SurvKit) is { } survKit && survKit.Parent != null)
                    {
                        var parent = survKit.Parent;
                        parent.RemoveWithoutRestrictions(survKit);
                        survKit.CurrentAddress = null;
                        SetCached(state, SurgicalItemType.SurvKit, null);
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[MedicalAnimations] survKit remove ex: {ex}"); }

            try
            {
                if (consumedItem != SurgicalItemType.CMS)
                {
                    if (GetCached(state, SurgicalItemType.CMS) is { } cmsKit && cmsKit.Parent != null)
                    {
                        var parent = cmsKit.Parent;
                        parent.RemoveWithoutRestrictions(cmsKit);
                        cmsKit.CurrentAddress = null;
                        SetCached(state, SurgicalItemType.CMS, null);
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[MedicalAnimations] cmsKit remove ex: {ex}"); }

            if (consumedItem == null)
            {
                SetCached(state, SurgicalItemType.SurvKit, null);
                SetCached(state, SurgicalItemType.CMS, null);
            }

            if (player.IsYourPlayer && consumedItem == null)
            {
                SetAnimationSpeed(player, 1f);
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

                // Keep fake surgical items bandage-like: one use, then EFT ApplyItem owns removal.
                if (item is MedsItemClass meds && meds.GetItemComponent<MedKitComponent>() is { } medKit)
                {
                    medKit.HpResource = 1f;
                    
                    if (medKit.IMedkitResource != null)
                    {
                        medKit.IMedkitResource = new FakeCmsResource(medKit.IMedkitResource);
                    }
                }

                if (!TryAddToQuest(player, item)) return null;

                // Pre-load the item's resources so ApplyItem can play the expected med animation
                // even when this template is not present elsewhere in the raid.
                if (Singleton<PoolManagerClass>.Instantiated)
                    _ = Singleton<PoolManagerClass>.Instance.LoadBundlesAndCreatePools(
                        PoolManagerClass.PoolsCategory.Raid, PoolManagerClass.AssemblyType.Online,
                        item.Template.AllResources.ToArray(), JobPriorityClass.Immediate);

                return item;
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[MedicalAnimations] CreateAndAttach failed: {ex.Message}"); return null; }
        }

        private static bool TryGetOrCreateCachedItem(Player player, SurgicalItemType itemType, out Item item)
        {
            item = null;
            ReviveDebug.Log("MedAnim_TryGetOrCreate_Enter", player?.ProfileId ?? "<null>", player?.IsYourPlayer ?? false, $"itemType={itemType}");
            if (!ValidatePlayer(player))
            {
                ReviveDebug.Log("MedAnim_ValidatePlayer_Fail", player?.ProfileId ?? "<null>", player?.IsYourPlayer ?? false, null);
                return false;
            }
            if (RMSession.GetPlayerState(player.ProfileId) is not { } state)
            {
                ReviveDebug.Log("MedAnim_RMSession_Fail", player.ProfileId, player.IsYourPlayer, null);
                return false;
            }

            item = GetCached(state, itemType);
            if (item != null)
            {
                if (!IsItemReferenceUsable(item) || IsItemDepleted(item))
                {
                    SetCached(state, itemType, null);
                    item = null;
                    ReviveDebug.Log("MedAnim_CachedInvalid", player.ProfileId, player.IsYourPlayer, $"itemType={itemType}");
                }
            }

            if (item != null)
            {
                ReviveDebug.Log("MedAnim_CachedHit", player.ProfileId, player.IsYourPlayer, $"itemType={itemType}");
                return true;
            }

            ReviveDebug.Log("MedAnim_CreateAndAttach_Call", player.ProfileId, player.IsYourPlayer, $"itemType={itemType}");
            item = CreateAndAttach(player, itemType);
            if (item != null)
            {
                ReviveDebug.Log("MedAnim_CreateAndAttach_Ok", player.ProfileId, player.IsYourPlayer, $"itemType={itemType} id={item.Id}");
                SetCached(state, itemType, item);
                return true;
            }

            ReviveDebug.Log("MedAnim_CreateAndAttach_Fail", player.ProfileId, player.IsYourPlayer, $"itemType={itemType}");
            Plugin.LogSource.LogWarning($"[MedicalAnimations] Failed to create fake {itemType} for {player.ProfileId}");
            return false;
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

        private static bool TryApplyInternal(Player player, Item item, float baseDuration, float speed, string label, Action onComplete)
        {
            ReviveDebug.Log("MedAnim_TryApplyInternal_Enter", player?.ProfileId ?? "<null>", player?.IsYourPlayer ?? false, $"label={label} speed={speed:F2}");
            try
            {
                ReviveDebug.Log("MedAnim_ApplyItem_Before", player.ProfileId, player.IsYourPlayer, $"label={label}");
                if (!Utils.TryApplyItemLikeTeamHeal(player, item, $"MedicalAnimations:{label}"))
                {
                    return false;
                }
                ReviveDebug.Log("MedAnim_ApplyItem_After", player.ProfileId, player.IsYourPlayer, $"label={label}");

                if (Mathf.Abs(speed - 1f) > 0.01f)
                {
                    ReviveDebug.Log("MedAnim_SetSpeedLater_Start", player.ProfileId, player.IsYourPlayer, $"speed={speed:F2}");
                    Plugin.StaticCoroutineRunner.StartCoroutine(SetSpeedLater(player, speed));
                }

                float resetDelay = (baseDuration / Mathf.Max(speed, 0.001f)) + SET_SPEED_DELAY + EARLY_FINISH_BUFFER;
                ReviveDebug.Log("MedAnim_ResetSpeedAfter_Start", player.ProfileId, player.IsYourPlayer, $"delay={resetDelay:F2}");
                Plugin.StaticCoroutineRunner.StartCoroutine(ResetSpeedAfter(player, resetDelay, onComplete));
                return true;
            }
            catch (Exception ex)
            {
                ReviveDebug.Log("MedAnim_ApplyItem_Exception", player?.ProfileId ?? "<null>", player?.IsYourPlayer ?? false, $"label={label} ex={ex.Message}");
                SetAnimationSpeed(player, 1f);
                Plugin.LogSource.LogWarning($"[MedicalAnimations] {label} ApplyItem failed: {ex.Message}");
                return false;
            }
        }

        private static IEnumerator SetSpeedLater(Player player, float speed)
        {
            yield return new WaitForSeconds(SET_SPEED_DELAY);
            SetAnimationSpeed(player, speed);
            ReviveDebug.Log("MedAnim_SetSpeedLater_Done", player?.ProfileId ?? "<null>", player?.IsYourPlayer ?? false, $"speed={speed:F2}");
        }

        private static IEnumerator ResetSpeedAfter(Player player, float delay, Action done)
        {
            ReviveDebug.Log("MedAnim_ResetSpeedAfter_Done", player?.ProfileId ?? "<null>", player?.IsYourPlayer ?? false, "before delay");
            yield return new WaitForSeconds(delay);
            ReviveDebug.Log("MedAnim_ResetSpeedAfter_Complete", player?.ProfileId ?? "<null>", player?.IsYourPlayer ?? false, null);
            SetAnimationSpeed(player, 1f);
            done?.Invoke();
        }

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

        private static bool IsItemReferenceUsable(Item item)
        {
            if (item == null) return false;

            try { return item.Parent != null; }
            catch { return false; }
        }

        private static bool IsItemDepleted(Item item)
        {
            if (item is not MedsItemClass meds) return false;

            try
            {
                var medKit = meds.GetItemComponent<MedKitComponent>();
                if (medKit != null) return medKit.HpResource <= float.Epsilon;
            }
            catch
            {
                // Ignore component read failures and treat as not depleted.
            }

            return false;
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

        public enum SurgicalItemType { SurvKit, CMS }
    }

    public class FakeCmsResource : IMedkitResource
    {
        private readonly IMedkitResource _wrapped;

        public FakeCmsResource(IMedkitResource wrapped)
        {
            _wrapped = wrapped;
        }

        public int MaxHpResource => _wrapped.MaxHpResource;

        // The core fix: Pretend we can heal missing HP instead of only destroyed limbs
        public float HpResourceRate => 1f;

        public EBodyPart[] BodyPartPriority => _wrapped.BodyPartPriority;
    }
}