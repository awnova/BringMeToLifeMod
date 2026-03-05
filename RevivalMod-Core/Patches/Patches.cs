//====================[ Imports ]====================
using System;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using Fika.Core.Main.ObservedClasses;
using Fika.Core.Main.Players;
using Fika.Core.Networking.Packets.Player.Common.SubPackets;
using HarmonyLib;
using KeepMeAlive.Components;
using KeepMeAlive.Features;
using KeepMeAlive.Helpers;
using SPT.Reflection.Patching;
using UnityEngine;

namespace KeepMeAlive.Patches
{
    //====================[ AvailableActionsPatch ]====================
    internal class AvailableActionsPatch : ModulePatch
    {
        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.FirstMethod(
                typeof(GetActionsClass),
                method => method.Name == nameof(GetActionsClass.GetAvailableActions) &&
                          method.GetParameters()[0].Name == "owner");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GamePlayerOwner owner, GInterface150 interactive, ref ActionsReturnClass __result)
        {
            if (interactive is BodyInteractable body)
            {
                if (body.Revivee == null || owner?.Player == null)
                {
                    Plugin.LogSource.LogError("AvailableActionsPatch: Revivee or Owner/Player is null");
                    return true;
                }
                Plugin.LogSource.LogDebug($"BodyInteractable.Revivee is {body.Revivee.ProfileId}, interactor is {owner.Player.ProfileId}");
                __result = body.GetActions(owner);
                return false;
            }

            if (interactive is MedPickerInteractable picker)
            {
                __result = picker.GetActions(owner);
                return false;
            }

            return true;
        }
    }

    //====================[ DeathPatch ]====================
    internal class DeathPatch : ModulePatch
    {
        //====================[ Fields ]====================
        private static readonly FieldInfo PlayerField = AccessTools.Field(typeof(ActiveHealthController), "Player");

        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill));

        [PatchPrefix]
        private static bool Prefix(ActiveHealthController __instance, EDamageType damageType)
        {
            try
            {
                if (PlayerField?.GetValue(__instance) is not Player player || player.IsAI) return true;

                string playerId = player.ProfileId;
                if (DeathMode.ShouldAllowDeathFromHardcoreHeadshot(__instance, damageType)) return true;

                if (RMSession.HasPlayerState(playerId))
                {
                    var state = RMSession.GetPlayerState(playerId).State;
                    if (state is RMState.BleedingOut or RMState.Reviving or RMState.Revived)
                    {
                        if (!player.IsYourPlayer && state == RMState.BleedingOut) return false;
                        if (DeathMode.ShouldBlockDeath(player, damageType)) return false;
                    }
                }

                if (DeathMode.ShouldBlockDeath(player, damageType))
                {
                    RevivalFeatures.SetPlayerCriticalState(player, true, damageType);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in Death prevention patch: {ex.Message}");
                return true;
            }
        }
    }

    //====================[ GameStartedPatch ]====================
    internal class GameStartedPatch : ModulePatch
    {
        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod() => typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted));

        [PatchPostfix]
        private static void PatchPostfix()
        {
            try
            {
                Plugin.LogSource.LogInfo("Game started");

                if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance.MainPlayer == null)
                {
                    Plugin.LogSource.LogError("GameWorld not instantiated or MainPlayer is null");
                    return;
                }

                RMSession.GetPlayerState(Singleton<GameWorld>.Instance.MainPlayer.ProfileId);
                Plugin.LogSource.LogDebug("Enabling body interactable");

                foreach (GameObject interact in Resources.FindObjectsOfTypeAll<GameObject>().Where(go => go.name.Contains("Body Interactable")))
                {
                    Plugin.LogSource.LogDebug($"Found interactable: {interact.name}");
                    interact.layer = LayerMask.NameToLayer("Interactive");
                    interact.GetComponent<BoxCollider>().enabled = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in GameStartedPatch: {ex.Message}");
            }
        }
    }

    //====================[ GhostModeHelper ]====================
    internal static class GhostModeHelper
    {
        //====================[ Helpers ]====================
        public static bool ShouldIgnore(IPlayer player)
        {
            try
            {
                return player != null && GhostMode.IsGhosted(player.ProfileId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] Error checking ghost state: {ex.Message}");
                return false;
            }
        }
    }

    //====================[ GhostModeGroupPatch ]====================
    internal class GhostModeGroupPatch : ModulePatch
    {
        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotsGroup), nameof(BotsGroup.AddEnemy), new[] { typeof(IPlayer), typeof(EBotEnemyCause) });

        [PatchPrefix]
        private static bool Prefix(IPlayer person, ref bool __result)
        {
            if (!GhostModeHelper.ShouldIgnore(person)) return true;
            __result = false;
            return false;
        }
    }

    //====================[ GhostModeMemoryPatch ]====================
    internal class GhostModeMemoryPatch : ModulePatch
    {
        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotMemoryClass), nameof(BotMemoryClass.AddEnemy), new[] { typeof(IPlayer), typeof(BotSettingsClass), typeof(bool) });

        [PatchPrefix]
        private static bool Prefix(IPlayer enemy) => !GhostModeHelper.ShouldIgnore(enemy);
    }

    //====================[ GhostModeSAINPatch ]====================
    internal class GhostModeSAINPatch : ModulePatch
    {
        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SAIN")?
                .GetType("SAIN.SAINComponent.Classes.EnemyClasses.EnemyListController");

            return type != null ? AccessTools.Method(type, "tryAddEnemy", new[] { typeof(IPlayer) }) : null;
        }

        [PatchPrefix]
        private static bool Prefix(IPlayer enemyPlayer, ref object __result)
        {
            if (!GhostModeHelper.ShouldIgnore(enemyPlayer)) return true;
            __result = null;
            return false;
        }
    }

    //====================[ HandleProceedPatch ]====================
    internal class HandleProceedPatch : ModulePatch
    {
        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ObservedPlayer), nameof(ObservedPlayer.HandleProceedPacket));

        [PatchPrefix]
        private static void Prefix(Player __instance)
        {
            try
            {
                if (__instance == null || __instance.IsYourPlayer) return;
                
                var state = RMSession.HasPlayerState(__instance.ProfileId) ? RMSession.GetPlayerState(__instance.ProfileId) : null;
                if (state != null && state.IsCritical)
                {
                    MedicalAnimations.EnsureFakeItemsForRemotePlayer(__instance);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[HandleProceedPatch] Prefix error: {ex.Message}");
            }
        }
    }

    //====================[ OnPlayerCreatedPatch ]====================
    internal class OnPlayerCreatedPatch : ModulePatch
    {
        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod() => AccessTools.Property(typeof(Player), nameof(Player.PlayerId)).GetSetMethod();

        [PatchPostfix]
        private static void Postfix(ref Player __instance)
        {
            if (__instance?.gameObject == null || __instance.gameObject.name.Contains("Bot")) return;
            AttachBodyInteractable(__instance);
        }

        //====================[ Interactable Builders ]====================
        private static void AttachBodyInteractable(Player player)
        {
            try
            {
                if (player?.gameObject?.transform == null)
                {
                    Plugin.LogSource.LogError("AttachBodyInteractable: Player or transform is null");
                    return;
                }

                var anchor = FindBackAnchor(player.gameObject.transform) ?? player.gameObject.transform;
                Plugin.LogSource.LogInfo($"Adding BodyInteractable to {player.PlayerId}");

                var obj = InteractableBuilder<BodyInteractable>.Build(
                    "Body Interactable", Vector3.zero, Vector3.one * RevivalModSettings.MEDICAL_RANGE.Value,
                    anchor, player, RevivalModSettings.TESTING.Value
                );

                var bodyInteractable = obj?.GetComponent<BodyInteractable>();
                if (bodyInteractable?.Revivee == null)
                {
                    Plugin.LogSource.LogError($"AttachBodyInteractable failed for {player.PlayerId}. Missing component or Revivee.");
                    return;
                }

                Plugin.LogSource.LogInfo($"Attached successfully. Revivee={bodyInteractable.Revivee.PlayerId}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"AttachBodyInteractable error for player {player?.ProfileId}: {ex.Message}");
            }
        }

        //====================[ Transform Finders ]====================
        private static Transform FindBackAnchor(Transform root)
        {
            // Try the direct path first (EFT player skeleton hierarchy)
            try
            {
                return root.GetChild(0).GetChild(4).GetChild(0).GetChild(2).GetChild(4).GetChild(0).GetChild(11);
            }
            catch { /* Skeleton changed or invalid, fall through to name search */ }

            // Fallback: search direct children for known spine bone names
            foreach (var name in new[] { "Spine3", "Spine2", "Spine1", "Back", "Spine", "Root" })
            {
                var t = root.Find(name);
                if (t != null) return t;
            }
            return null;
        }
    }

    //====================[ FikaHealthSyncLocalPlayerGuardPatch ]====================
    /// <summary>
    /// Silently discards HealthSyncPackets that arrive at the local player's FikaClient for the
    /// local player itself. In a Fika headless-relay setup the server echoes every client's own
    /// HealthSync packets back to that client. Because the local FikaPlayer is not an
    /// ObservedPlayer, Fika logs a spam error for each echoed packet. The local player's health
    /// effects are already applied by ClientHealthController — receiving them a second time via
    /// HandleSyncPacket is incorrect and must be suppressed.
    /// </summary>
    internal class FikaHealthSyncLocalPlayerGuardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(HealthSyncPacket), nameof(HealthSyncPacket.Execute));

        [PatchPrefix]
        // 'player' is the first parameter of HealthSyncPacket.Execute(FikaPlayer player);
        // we match by position so we can use the base EFT.Player type without a hard reference.
        private static bool Prefix(Player player)
        {
            // Allow processing for observed players (normal case) and null (safety).
            // Discard packets directed at the local player — they were echoed back by the
            // headless relay and would cause a Fika "was not observed" error spam.
            return player == null || !player.IsYourPlayer;
        }
    }

    //====================[ SpecialSlotDefibPatch ]====================
    internal class SpecialSlotDefibPatch : ModulePatch
    {
        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Slot), nameof(Slot.CheckCompatibility));

        [PatchPostfix]
        private static void Postfix(Slot __instance, Item item, ref bool __result)
        {
            if (__result || item == null || !__instance.IsSpecial) return;

            var revivalTpl = RevivalModSettings.REVIVAL_ITEM_ID?.Value ?? "5c052e6986f7746b207bc3c9";
            var itemTpl = item.StringTemplateId ?? (string)item.TemplateId;

            if (!string.IsNullOrEmpty(revivalTpl) && string.Equals(itemTpl, revivalTpl, StringComparison.OrdinalIgnoreCase))
            {
                __result = true;
            }
        }
    }
}

namespace KeepMeAlive.Features
{
    //====================[ RevivalFeatures ]====================
    internal class RevivalFeatures : ModulePatch
    {
        //====================[ Patching / Lifecycle ]====================
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.UpdateTick));

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            try
            {
                DownedStateController.TickBodyInteractableColliderState(__instance);
                DownedStateController.TickInvulnerability(__instance);
                DownedStateController.TickCooldown(__instance);
                DownedStateController.TickResync(__instance);

                if (!__instance.IsYourPlayer) return;

                if (RevivalModSettings.TESTING.Value) CheckTestKeybinds(__instance);

                DownedStateController.TickDowned(__instance);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in RevivalFeatures patch: {ex.Message}");
            }
        }

        //====================[ Keybinds ]====================
        private static void CheckTestKeybinds(Player player)
        {
            try
            {
                void TrySurg(KeyCode key, MedicalAnimations.SurgicalItemType type)
                {
                    if (!Input.GetKeyDown(key)) return;
                    MedicalAnimations.CreateInQuestInventory(player, type);
                    MedicalAnimations.UseAtSpeed(player, type, 1f);
                }

                void ToggleGhost(KeyCode key, bool enter)
                {
                    if (!Input.GetKeyDown(key)) return;
                    
                    if (enter) GhostMode.EnterGhostModeById(player.ProfileId);
                    else GhostMode.ExitGhostModeById(player.ProfileId);

                    string stateStr = enter ? "Entered (F7)" : "Exited (F8)";
                    NotificationManagerClass.DisplayMessageNotification(
                        $"GhostMode: {stateStr}", ENotificationDurationType.Default, ENotificationIconType.Default, Color.cyan);
                }

                TrySurg(KeyCode.F3, MedicalAnimations.SurgicalItemType.SurvKit);
                TrySurg(KeyCode.F4, MedicalAnimations.SurgicalItemType.CMS);
                ToggleGhost(KeyCode.F7, true);
                ToggleGhost(KeyCode.F8, false);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TestKeybinds] Error: {ex.Message}");
            }
        }

        //====================[ Wrapper Methods ]====================
        public static bool IsPlayerInCriticalState(string playerId) => DownedStateController.IsPlayerInCriticalState(playerId);
        public static bool IsPlayerInvulnerable(string playerId) => DownedStateController.IsPlayerInvulnerable(playerId);
        public static bool IsRevivalOnCooldown(string playerId) => DownedStateController.IsRevivalOnCooldown(playerId);
        public static void SetPlayerCriticalState(Player player, bool isCritical, EDamageType damageType) => DownedStateController.SetPlayerCriticalState(player, isCritical, damageType);
    }

    //====================[ TeamHealGEventArgs13SuppressPatch ]====================
    // MedEffect.Added/UpdateResource fire a GEventArgs13 event on the item owner's
    // InventoryController.  When the item belongs to a remote healer it lives in
    // an ObservedInventoryController on the patient's machine, which has its own
    // GInterface187 (OnDrain) handlers tied to the observed FirearmController.
    // Receiving that event triggers RemoveAmmoFromChamber on an observed player
    // whose animation state is not ready → NullRef → every subsequent SwapOperation
    // for that player fails with "hands controller can't perform this operation".
    //
    // IMPORTANT: revival fake CMS/SurvKit items are also stored in ObservedInventoryController
    // (via EnsureFakeItemsForRemotePlayer).  NetworkHealthControllerAbstractClass.MedEffect
    // calls the SAME path for those items.  We MUST allow them through so the network
    // effect lifecycle (UpdateResource → MedItem null-out) works correctly.
    internal class TeamHealGEventArgs13SuppressPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(TraderControllerClass), "RaiseEvent",
                new[] { typeof(GEventArgs13) });

        [PatchPrefix]
        private static bool Prefix(TraderControllerClass __instance, GEventArgs13 args)
        {
            // Allow non-ObservedInventoryController owners (local player, bots, etc.)
            if (__instance is not ObservedInventoryController) return true;
            // Allow revival fake items (CMS dea2…, SurvKit dea1…) through — their
            // NetworkHealthControllerAbstractClass.MedEffect depends on this event.
            if (MedicalAnimations.IsFakeRevivalItem(args?.Item)) return true;
            // Suppress for all other foreign items in ObservedInventoryController
            // (i.e. team-heal items from the healer that would crash the observed firearm controller).
            return false;
        }
    }

    //====================[ TeamHealRemoveItemSuppressPatch ]====================
    // GClass3017.RemoveItem is called by NetworkHealthControllerAbstractClass.MedEffect.UpdateResource
    // when a single-use med item is exhausted via HealthSyncPacket.  When the item belongs to
    // a remote healer (parent owned by ObservedInventoryController), this would silently remove
    // it only from the patient's local replica — the healer's real inventory is unchanged.
    // Suppressing the call here lets the healer's own Fika-synced ConsumeItem transaction handle
    // the authoritative removal so every client sees a consistent state.
    //
    // IMPORTANT: revival fake CMS/SurvKit items also live in ObservedInventoryController and
    // go through the same code path.  We MUST allow them through so the fake item is properly
    // removed from QuestRaidItems when the server signals exhaustion, keeping NullOutNetworkMedItem
    // able to find and clean up the effect while MedItem is still non-null.
    internal class TeamHealRemoveItemSuppressPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(GClass3017), "RemoveItem");

        [PatchPrefix]
        private static bool Prefix(Item item)
        {
            // Allow non-ObservedInventoryController owners.
            if (item?.Parent?.GetOwner() is not ObservedInventoryController) return true;
            // Allow revival fake items — their removal from QuestRaidItems must happen
            // normally so NullOutNetworkMedItem can find the effect before MedItem is null'ed.
            if (MedicalAnimations.IsFakeRevivalItem(item)) return true;
            // Suppress removal for foreign team-heal items in ObservedInventoryController.
            return false;
        }
    }
}