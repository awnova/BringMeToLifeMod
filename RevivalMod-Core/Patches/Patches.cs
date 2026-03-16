//====================[ Imports ]====================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using Fika.Core.Main.Players;
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

                // Cache the block evaluation so we don't calculate it twice
                bool shouldBlockDeath = DeathMode.ShouldBlockDeath(player, damageType);

                if (RMSession.HasPlayerState(playerId))
                {
                    var state = RMSession.GetPlayerState(playerId).State;
                    if (state is RMState.BleedingOut or RMState.Reviving or RMState.Revived)
                    {
                        if (!player.IsYourPlayer && state == RMState.BleedingOut) return false;
                        if (shouldBlockDeath) return false;
                    }
                }

                if (shouldBlockDeath)
                {
                    DownedStateController.SetPlayerCriticalState(player, true, damageType);
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
                    foreach (var col in interact.GetComponents<Collider>())
                    {
                        if (col != null) col.enabled = true;
                    }
                }

                // Inject defib icon eagerly so FikaHealthBar nameplates can show it.
                // FikaHealthBar.AddEffect() looks up effect.Type in _effectIcons at the
                // moment EffectAddedEvent fires — if the icon isn't injected yet it's
                // silently skipped with no retry. OnGameStarted fires before any revival
                // can occur, so injecting here guarantees it's ready in time.
                DefibCooldownIconPatch.EnsureIconInjected();
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
                    anchor, player, RevivalModSettings.FREE_TEAM_HEALING.Value
                );

                var bodyInteractable = obj?.GetComponent<BodyInteractable>();
                if (bodyInteractable != null)
                {
                    bodyInteractable.Revivee = player;
                }

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
                BodyInteractableManager.Tick(__instance);
                PostRevivalController.TickInvulnerability(__instance);
                PostRevivalController.TickCooldown(__instance);
                DownedStateController.TickResync(__instance);

                if (!__instance.IsYourPlayer) return;

                if (RMSession.HasPlayerState(__instance.ProfileId))
                {
                    var st = RMSession.GetPlayerState(__instance.ProfileId);
                    if (st.IsCritical)
                    {
                        ReviveDebug.Log("ReviveTick_Enter", __instance.ProfileId, __instance.IsYourPlayer, $"state={st.State}");
                        ReviveDebug.Log("ReviveTick_CallTickDowned", __instance.ProfileId, __instance.IsYourPlayer, null);
                    }
                }

                if (RevivalModSettings.DEBUG_KEYBINDS.Value) CheckTestKeybinds(__instance);

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
    }

    //====================[ DefibCooldownIconPatch ]====================
    // Injects the DefibCooldown icon sprite into EFT's EffectIcons registry on the
    // first EffectsPanel.Show() call in-raid.
    //
    // WHY NOT OnAfterDeserialize: StaticIcons is deserialized during Unity's asset-loading
    // phase, before BepInEx plugins load — the method has already fired by the time our
    // patch is registered. EffectsPanel.Show() fires lazily in-raid and is always after
    // plugin startup, so we inject there once and cache _injected = true for all later calls.
    internal class DefibCooldownIconPatch : ModulePatch
    {
        private static bool _injected;
        private static Sprite _cachedSprite;

        protected override MethodBase GetTargetMethod()
        {
            var method = AccessTools.Method(typeof(EffectsPanel), "Show");
            if (method == null)
                Plugin.LogSource.LogError("[DefibCooldownIconPatch] Could not find EffectsPanel.Show.");
            else
                Plugin.LogSource.LogDebug("[DefibCooldownIconPatch] Patch enabled on EffectsPanel.Show.");
            return method;
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            if (_injected) return;
            EnsureIconInjected();
        }

        internal static void EnsureIconInjected()
        {
            try
            {
                var iconDict = EFTHardSettings.Instance?.StaticIcons?.EffectIcons?.EffectIcons;
                if (iconDict == null)
                {
                    Plugin.LogSource.LogWarning("[DefibCooldownIconPatch] EffectIcons dict not yet available.");
                    return; // Don't set _injected; retry on next EffectsPanel.Show()
                }

                if (_cachedSprite == null)
                    _cachedSprite = LoadDefibSprite();

                if (_cachedSprite == null)
                {
                    Plugin.LogSource.LogWarning("[DefibCooldownIconPatch] Custom sprite unavailable; using fallback.");
                    foreach (var s in iconDict.Values)
                        if (s != null) { _cachedSprite = s; break; }
                }

                if (_cachedSprite == null)
                {
                    Plugin.LogSource.LogError("[DefibCooldownIconPatch] No sprite available — icon will not show.");
                    _injected = true;
                    return;
                }

                iconDict[typeof(IDefibCooldown)] = _cachedSprite;
                Plugin.LogSource.LogInfo(
                    $"[DefibCooldownIconPatch] DefibCooldown icon injected (dict size now {iconDict.Count}).");
                _injected = true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[DefibCooldownIconPatch] EnsureIconInjected error: {ex}");
            }
        }

        private static Sprite LoadDefibSprite()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                const string resource = "RevivalMod.Resources.defib_cooldown.png";

                using var stream = asm.GetManifestResourceStream(resource);
                if (stream == null)
                {
                    Plugin.LogSource.LogWarning(
                        $"[DefibCooldownIconPatch] Embedded resource '{resource}' not found.");
                    return null;
                }

                byte[] bytes;
                using (var br = new System.IO.BinaryReader(stream))
                    bytes = br.ReadBytes((int)stream.Length);

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    Plugin.LogSource.LogWarning("[DefibCooldownIconPatch] Texture2D.LoadImage returned false.");
                    return null;
                }

                Plugin.LogSource.LogDebug(
                    $"[DefibCooldownIconPatch] Loaded defib PNG: {tex.width}x{tex.height}");
                return Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[DefibCooldownIconPatch] LoadDefibSprite error: {ex}");
                return null;
            }
        }
    }
}