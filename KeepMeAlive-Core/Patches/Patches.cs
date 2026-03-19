//====================[ Imports ]====================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using KeepMeAlive.Components;
using KeepMeAlive.Features;
using KeepMeAlive.Helpers;
using Fika.Core.Main.Components;
using Fika.Core.Main.Players;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.UI;

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
            if (BodyInteractableRuntime.TryRouteActions(owner, interactive, ref __result))
            {
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

    //====================[ DownedWeaponProceedBlockPatch ]====================
    internal class DownedWeaponProceedBlockPatch : ModulePatch
    {
        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player), nameof(Player.Proceed), new[]
            {
                typeof(Weapon),
                typeof(Callback<IFirearmHandsController>),
                typeof(bool)
            });

        [PatchPrefix]
        private static bool Prefix(Player __instance, Weapon weapon)
        {
            try
            {
                if (__instance == null || weapon == null || __instance.IsAI)
                {
                    return true;
                }

                if (!RMSession.HasPlayerState(__instance.ProfileId))
                {
                    return true;
                }

                var st = RMSession.GetPlayerState(__instance.ProfileId);
                bool shouldBlock = (st.State is RMState.BleedingOut or RMState.Reviving) && !st.AllowWeaponEquipForReviveAnim;
                if (!shouldBlock)
                {
                    return true;
                }

                Plugin.LogSource.LogDebug($"[DownedWeaponBlock] Blocked weapon proceed for {__instance.ProfileId} in state={st.State}, beingRevived={st.IsBeingRevived}, selfReviving={st.IsSelfReviving}");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[DownedWeaponBlock] Prefix error: {ex.Message}");
                return true;
            }
        }
    }

    //====================[ DownedClientWeaponProceedBlockPatch ]====================
    internal class DownedClientWeaponProceedBlockPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ClientPlayer), nameof(Player.Proceed), new[]
            {
                typeof(Weapon),
                typeof(Callback<IFirearmHandsController>),
                typeof(bool)
            });

        [PatchPrefix]
        private static bool Prefix(Player __instance, Weapon weapon)
        {
            try
            {
                if (__instance == null || weapon == null || __instance.IsAI) return true;
                if (!RMSession.HasPlayerState(__instance.ProfileId)) return true;

                var st = RMSession.GetPlayerState(__instance.ProfileId);
                bool shouldBlock = (st.State is RMState.BleedingOut or RMState.Reviving) && !st.AllowWeaponEquipForReviveAnim;
                if (!shouldBlock) return true;

                Plugin.LogSource.LogDebug($"[DownedWeaponBlock] Blocked ClientPlayer weapon proceed for {__instance.ProfileId} in state={st.State}, beingRevived={st.IsBeingRevived}, selfReviving={st.IsSelfReviving}");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[DownedWeaponBlock] ClientPlayer prefix error: {ex.Message}");
                return true;
            }
        }
    }

    //====================[ DownedFikaWeaponProceedBlockPatch ]====================
    internal class DownedFikaWeaponProceedBlockPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(FikaPlayer), nameof(Player.Proceed), new[]
            {
                typeof(Weapon),
                typeof(Callback<IFirearmHandsController>),
                typeof(bool)
            });

        [PatchPrefix]
        private static bool Prefix(Player __instance, Weapon weapon)
        {
            try
            {
                if (__instance == null || weapon == null || __instance.IsAI) return true;
                if (!RMSession.HasPlayerState(__instance.ProfileId)) return true;

                var st = RMSession.GetPlayerState(__instance.ProfileId);
                bool shouldBlock = (st.State is RMState.BleedingOut or RMState.Reviving) && !st.AllowWeaponEquipForReviveAnim;
                if (!shouldBlock) return true;

                Plugin.LogSource.LogDebug($"[DownedWeaponBlock] Blocked FikaPlayer weapon proceed for {__instance.ProfileId} in state={st.State}, beingRevived={st.IsBeingRevived}, selfReviving={st.IsSelfReviving}");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[DownedWeaponBlock] FikaPlayer prefix error: {ex.Message}");
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
                Plugin.LogSource.LogDebug("Raid started - MainPlayer state initialized.");

                // Inject reviveItem icon eagerly so FikaHealthBar nameplates can show it.
                // FikaHealthBar.AddEffect() looks up effect.Type in _effectIcons at the
                // moment EffectAddedEvent fires ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â if the icon isn't injected yet it's
                // silently skipped with no retry. OnGameStarted fires before any revival
                // can occur, so injecting here guarantees it's ready in time.
                ReviveItemCooldownIconPatch.EnsureIconInjected();

                // Warm up UI panel references during raid load. This avoids first-use races
                // where the first downed event lands before panel hierarchy is initialized.
                Plugin.StaticCoroutineRunner.StartCoroutine(WarmupUiPanelsCoroutine());
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in GameStartedPatch: {ex.Message}");
            }
        }

        private static IEnumerator WarmupUiPanelsCoroutine()
        {
            const int maxFrames = 3600; // ~60 seconds at 60 FPS

            for (int i = 0; i < maxFrames; i++)
            {
                if (MonoBehaviourSingleton<GameUI>.Instantiated)
                {
                    var gameUi = MonoBehaviourSingleton<GameUI>.Instance;
                    bool uiHierarchyReady = gameUi != null
                        && gameUi.gameObject.activeInHierarchy
                        && gameUi.LocationTransitTimerPanel != null;

                    if (uiHierarchyReady && VFX_UI.TryWarmupPanels())
                    {
                        Plugin.LogSource.LogDebug("[GameStarted] UI panel warmup complete (GameUI hierarchy active).");
                        yield break;
                    }
                }

                yield return null;
            }

            Plugin.LogSource.LogWarning("[GameStarted] UI panel warmup timed out before GameUI hierarchy became ready; runtime panel retry remains enabled.");
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
        private static void Postfix(Player __instance)
        {
            if (__instance?.gameObject == null || __instance.IsAI || __instance is FikaBot) return;
            BodyInteractableRuntime.AttachToPlayer(__instance);
        }
    }

    //====================[ SpecialSlotReviveItemPatch ]====================
    internal class SpecialSlotReviveItemPatch : ModulePatch
    {
        //====================[ Patching ]====================
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Slot), nameof(Slot.CheckCompatibility));

        [PatchPostfix]
        private static void Postfix(Slot __instance, Item item, ref bool __result)
        {
            if (__result || item == null || !__instance.IsSpecial) return;

            var revivalTpl = KeepMeAliveSettings.REVIVAL_ITEM_ID?.Value ?? "5c052e6986f7746b207bc3c9";
            var itemTpl = item.StringTemplateId ?? (string)item.TemplateId;

            if (!string.IsNullOrEmpty(revivalTpl) && string.Equals(itemTpl, revivalTpl, StringComparison.OrdinalIgnoreCase))
            {
                __result = true;
            }
        }
    }

    //====================[ InventoryScreenInputBlockPatch ]====================
    // Prevents the local player from opening the real inventory screen while the
    // silent revive animation is playing.  If they could open it, closing it would
    // fire the EftGamePlayerOwner exit callback which calls SetInventoryOpened(false)
    // and terminate the revive animation mid-revive.
    internal class InventoryScreenInputBlockPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(EftGamePlayerOwner), nameof(GamePlayerOwner.TranslateInventoryScreenInput));

        [PatchPrefix]
        private static bool Prefix(GamePlayerOwner __instance, ref bool __result)
        {
            try
            {
                if (__instance?.Player == null) return true;
                if (!RMSession.HasPlayerState(__instance.Player.ProfileId)) return true;
                var st = RMSession.GetPlayerState(__instance.Player.ProfileId);
                if (st.State == RMState.Reviving || st.IsSilentInventoryAnimActive)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[InventoryInputBlock] error: {ex.Message}");
            }
            return true;
        }
    }

    //====================[ SilentInventoryCommandBlockPatch ]====================
    // While silent inventory revive animation is active, consume all gameplay commands
    // so left-click cannot fire and break the expected inventory behavior.
    internal class SilentInventoryCommandBlockPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(EftGamePlayerOwner), nameof(EftGamePlayerOwner.TranslateCommand));

        [PatchPrefix]
        private static bool Prefix(EftGamePlayerOwner __instance, ref EFT.InputSystem.InputNode.ETranslateResult __result)
        {
            try
            {
                if (__instance?.Player == null) return true;
                if (!RMSession.HasPlayerState(__instance.Player.ProfileId)) return true;

                var st = RMSession.GetPlayerState(__instance.Player.ProfileId);
                if (!st.IsSilentInventoryAnimActive) return true;

                __result = EFT.InputSystem.InputNode.ETranslateResult.BlockAll;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[SilentInvCommandBlock] error: {ex.Message}");
                return true;
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
                BodyInteractableRuntime.Tick(__instance);
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

                if (KeepMeAliveSettings.DEBUG_KEYBINDS.Value) CheckTestKeybinds(__instance);

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
                void ToggleGhost(KeyCode key, bool enter)
                {
                    if (!Input.GetKeyDown(key)) return;
                    
                    if (enter) GhostMode.EnterGhostModeById(player.ProfileId);
                    else GhostMode.ExitGhostModeById(player.ProfileId);

                    string stateStr = enter ? "Entered (F7)" : "Exited (F8)";
                    NotificationManagerClass.DisplayMessageNotification(
                        $"GhostMode: {stateStr}", ENotificationDurationType.Default, ENotificationIconType.Default, Color.cyan);
                }

                ToggleGhost(KeyCode.F7, true);
                ToggleGhost(KeyCode.F8, false);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TestKeybinds] Error: {ex.Message}");
            }
        }
    }

    //====================[ ReviveItemCooldownIconPatch ]====================
    // Injects the ReviveItemCooldown icon sprite into EFT's EffectIcons registry on the
    // first EffectsPanel.Show() call in-raid.
    //
    // WHY NOT OnAfterDeserialize: StaticIcons is deserialized during Unity's asset-loading
    // phase, before BepInEx plugins load ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â the method has already fired by the time our
    // patch is registered. EffectsPanel.Show() fires lazily in-raid and is always after
    // plugin startup, so we inject there once and cache _injected = true for all later calls.
    internal class ReviveItemCooldownIconPatch : ModulePatch
    {
        private static bool _injected;
        private static bool _hardSettingsLoadStarted;
        private static bool _hardSettingsLoadCompleted;
        private static Sprite _cachedSprite;

        protected override MethodBase GetTargetMethod()
        {
            var method = AccessTools.Method(typeof(EffectsPanel), "Show");
            if (method == null)
                Plugin.LogSource.LogError("[ReviveItemCooldownIconPatch] Could not find EffectsPanel.Show.");
            else
                Plugin.LogSource.LogDebug("[ReviveItemCooldownIconPatch] Patch enabled on EffectsPanel.Show.");
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
                if (_injected)
                    return;

                if (!_hardSettingsLoadCompleted)
                {
                    StartHardSettingsLoad();
                    return;
                }

                var iconDict = EFTHardSettings.Instance?.StaticIcons?.EffectIcons?.EffectIcons;
                if (iconDict == null)
                {
                    Plugin.LogSource.LogWarning("[ReviveItemCooldownIconPatch] EffectIcons dict not yet available.");
                    return; // Don't set _injected; retry on next EffectsPanel.Show()
                }

                if (_cachedSprite == null)
                    _cachedSprite = LoadReviveItemSprite();

                if (_cachedSprite == null)
                {
                    Plugin.LogSource.LogWarning("[ReviveItemCooldownIconPatch] Custom sprite unavailable; using fallback.");
                    foreach (var s in iconDict.Values)
                        if (s != null) { _cachedSprite = s; break; }
                }

                if (_cachedSprite == null)
                {
                    Plugin.LogSource.LogError("[ReviveItemCooldownIconPatch] No sprite available ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â icon will not show.");
                    _injected = true;
                    return;
                }

                iconDict[typeof(IReviveItemCooldown)] = _cachedSprite;
                Plugin.LogSource.LogInfo(
                    $"[ReviveItemCooldownIconPatch] ReviveItemCooldown icon injected (dict size now {iconDict.Count}).");
                _injected = true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[ReviveItemCooldownIconPatch] EnsureIconInjected error: {ex}");
            }
        }

        private static void StartHardSettingsLoad()
        {
            if (_hardSettingsLoadStarted)
                return;

            if (Plugin.StaticCoroutineRunner == null)
            {
                Plugin.LogSource.LogWarning(
                    "[ReviveItemCooldownIconPatch] Coroutine runner unavailable; deferring EFTHardSettings.Load.");
                return;
            }

            _hardSettingsLoadStarted = true;
            Plugin.StaticCoroutineRunner.StartCoroutine(LoadHardSettingsAndInject());
        }

        private static IEnumerator LoadHardSettingsAndInject()
        {
            var loadTask = EFTHardSettings.Load();
            while (!loadTask.IsCompleted)
                yield return null;

            if (loadTask.IsFaulted || loadTask.IsCanceled)
            {
                Plugin.LogSource.LogError("[ReviveItemCooldownIconPatch] EFTHardSettings.Load failed or was canceled.");
                _hardSettingsLoadStarted = false;
                yield break;
            }

            _hardSettingsLoadCompleted = true;
            EnsureIconInjected();
        }

        private static Sprite LoadReviveItemSprite()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                const string resource = "KeepMeAlive.Resources.revive_item_cooldown.png";

                using var stream = asm.GetManifestResourceStream(resource);
                if (stream == null)
                {
                    Plugin.LogSource.LogWarning(
                        $"[ReviveItemCooldownIconPatch] Embedded resource '{resource}' not found.");
                    return null;
                }

                byte[] bytes;
                using (var br = new System.IO.BinaryReader(stream))
                    bytes = br.ReadBytes((int)stream.Length);

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    Plugin.LogSource.LogWarning("[ReviveItemCooldownIconPatch] Texture2D.LoadImage returned false.");
                    return null;
                }

                Plugin.LogSource.LogDebug(
                    $"[ReviveItemCooldownIconPatch] Loaded reviveItem PNG: {tex.width}x{tex.height}");
                return Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[ReviveItemCooldownIconPatch] LoadReviveItemSprite error: {ex}");
                return null;
            }
        }
    }

    //====================[ FikaOverlayPatch ]====================
    // Patches FikaHealthBar.Create to attach a per-player overlay controller.
    // The controller replaces the entire Fika nameplate with a state-specific
    // image (BleedingOut or Reviving) for that one observed player only.
    internal class FikaOverlayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(FikaHealthBar), "Create", new[] { typeof(ObservedPlayer) });

        [PatchPostfix]
        private static void PatchPostfix(FikaHealthBar __result, ObservedPlayer player)
        {
            try
            {
                if (__result == null || player == null) return;
                var controller = __result.gameObject.AddComponent<FikaOverlayController>();
                controller.Initialize(__result, player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[FikaOverlayPatch] Postfix error: {ex.Message}");
            }
        }
    }

    //====================[ FikaOverlayController ]====================
    // Per-player MonoBehaviour that swaps the Fika overhead panel content
    // with a single image based on the observed player's RMState.
    // Runs in LateUpdate to override Fika's Update-phase alpha writes.
    // Respects Fika's own occlusion/ADS/distance hiding and nameplate toggles.
    //
    // All PlayerPlateUI field access uses reflection because the hollowed
    // reference DLL strips those members at compile time.
    internal sealed class FikaOverlayController : MonoBehaviour
    {
        // Reflect into FikaHealthBar's private cached references.
        private static readonly FieldInfo PlayerPlateField =
            AccessTools.Field(typeof(FikaHealthBar), "_playerPlate");
        private static readonly FieldInfo LabelsGroupField =
            AccessTools.Field(typeof(FikaHealthBar), "_labelsGroup");
        private static readonly FieldInfo StatusGroupField =
            AccessTools.Field(typeof(FikaHealthBar), "_statusGroup");

        // Reflect into PlayerPlateUI for ScalarObjectScreen (not exposed by hollowed DLL).
        private static readonly FieldInfo ScalarObjectScreenField =
            AccessTools.Field(typeof(PlayerPlateUI), "ScalarObjectScreen");

        private static Sprite _bleedingOutSprite;
        private static Sprite _revivingSprite;
        private static bool _spritesLoaded;
        private const float OverlayMaxWidth = 374f;  // native art size (1:1 scale)
        private const float OverlayMaxHeight = 130f;

        private FikaHealthBar _healthBar;
        private string _observedProfileId;

        // Cached per-instance references resolved once via reflection.
        private bool _resolved;
        private PlayerPlateUI _playerPlate;
        private CanvasGroup _labelsGroup;
        private CanvasGroup _statusGroup;
        private GameObject _scalarScreen;

        private GameObject _overlayObject;
        private RectTransform _overlayRect;
        private Image _overlayImage;
        private Sprite _lastAppliedSprite;
        private Sprite _cachedTargetSprite;
        private bool _subscribedToStateEvents;
        private bool _isContentSuppressed;

        public void Initialize(FikaHealthBar healthBar, ObservedPlayer player)
        {
            _healthBar = healthBar;
            _observedProfileId = player?.ProfileId;
            EnsureSpritesLoaded();

            if (!string.IsNullOrEmpty(_observedProfileId) && RMSession.HasPlayerState(_observedProfileId))
            {
                var current = RMSession.GetPlayerState(_observedProfileId);
                _cachedTargetSprite = MapStateToSprite(current?.State ?? RMState.None);
            }
        }

        private void OnEnable()
        {
            if (_subscribedToStateEvents) return;
            RMSession.PlayerStateChanged += OnPlayerStateChanged;
            _subscribedToStateEvents = true;
        }

        private void LateUpdate()
        {
            // Healthbar destroyed ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â clean up.
            if (_healthBar == null)
            {
                Destroy(this);
                return;
            }

            // Resolve reflected references once.
            if (!_resolved)
            {
                _playerPlate = PlayerPlateField?.GetValue(_healthBar) as PlayerPlateUI;
                if (_playerPlate == null) return; // Not yet initialized by Fika.

                _labelsGroup = LabelsGroupField?.GetValue(_healthBar) as CanvasGroup;
                _statusGroup = StatusGroupField?.GetValue(_healthBar) as CanvasGroup;
                _scalarScreen = ScalarObjectScreenField?.GetValue(_playerPlate) as GameObject;
                _resolved = true;
            }

            var targetSprite = _cachedTargetSprite;
            if (targetSprite == null)
            {
                _lastAppliedSprite = null;
                RestoreVanillaPlate();
                return;
            }

            // If Fika's plate root is inactive (occlusion, ADS, distance, nameplate toggle)
            // then our overlay must also stay hidden ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â no through-wall artifacts.
            if (_scalarScreen == null || !_scalarScreen.activeSelf ||
                _playerPlate == null || !_playerPlate.gameObject.activeSelf)
            {
                SetOverlayActive(false);
                return;
            }

            SetContentSuppressed(true);

            if (_lastAppliedSprite != targetSprite)
                _lastAppliedSprite = targetSprite;

            ShowOverlay(targetSprite);
        }

        private void OnPlayerStateChanged(string playerId, RMState oldState, RMState newState)
        {
            if (string.IsNullOrEmpty(_observedProfileId)) return;
            if (!string.Equals(playerId, _observedProfileId, StringComparison.Ordinal)) return;

            _cachedTargetSprite = MapStateToSprite(newState);
        }

        private static Sprite MapStateToSprite(RMState state)
        {
            return state switch
            {
                RMState.BleedingOut => _bleedingOutSprite,
                RMState.Reviving => _revivingSprite,
                _ => null
            };
        }

        private void OnDisable()
        {
            if (_subscribedToStateEvents)
            {
                RMSession.PlayerStateChanged -= OnPlayerStateChanged;
                _subscribedToStateEvents = false;
            }
        }

        private void SetContentSuppressed(bool suppressed)
        {
            if (_isContentSuppressed == suppressed)
                return;

            _isContentSuppressed = suppressed;

            if (_labelsGroup?.gameObject != null)
                _labelsGroup.gameObject.SetActive(!suppressed);
            if (_statusGroup?.gameObject != null)
                _statusGroup.gameObject.SetActive(!suppressed);
        }

        private void ShowOverlay(Sprite sprite)
        {
            if (_overlayObject == null)
                CreateOverlayObject();

            if (_overlayObject == null) return;

            if (_overlayImage.sprite != sprite)
                _overlayImage.sprite = sprite;

            ApplyOverlaySizeForSprite(sprite);

            SetOverlayActive(true);
        }

        private void ApplyOverlaySizeForSprite(Sprite sprite)
        {
            if (_overlayRect == null || sprite == null)
                return;

            var spriteRect = sprite.rect;
            if (spriteRect.width <= 0f || spriteRect.height <= 0f)
            {
                _overlayRect.sizeDelta = new Vector2(OverlayMaxWidth, OverlayMaxHeight);
                return;
            }

            float targetWidth = OverlayMaxWidth;
            float targetHeight = targetWidth * (spriteRect.height / spriteRect.width);

            if (targetHeight > OverlayMaxHeight)
            {
                targetHeight = OverlayMaxHeight;
                targetWidth = targetHeight * (spriteRect.width / spriteRect.height);
            }

            var current = _overlayRect.sizeDelta;
            if (Mathf.Abs(current.x - targetWidth) > 0.1f || Mathf.Abs(current.y - targetHeight) > 0.1f)
                _overlayRect.sizeDelta = new Vector2(targetWidth, targetHeight);
        }

        private void RestoreVanillaPlate()
        {
            SetOverlayActive(false);
            SetContentSuppressed(false);
        }

        private void SetOverlayActive(bool active)
        {
            if (_overlayObject != null && _overlayObject.activeSelf != active)
                _overlayObject.SetActive(active);
        }

        private void CreateOverlayObject()
        {
            var root = _scalarScreen?.transform;
            if (root == null) return;

            _overlayObject = new GameObject("KMA_FikaOverlay", typeof(RectTransform), typeof(Image));
            _overlayObject.transform.SetParent(root, false);

            _overlayRect = _overlayObject.GetComponent<RectTransform>();
            _overlayRect.anchorMin = new Vector2(0.5f, 0.5f);
            _overlayRect.anchorMax = new Vector2(0.5f, 0.5f);
            _overlayRect.pivot = new Vector2(0.5f, 0.5f);
            _overlayRect.anchoredPosition = Vector2.zero;
            _overlayRect.sizeDelta = new Vector2(OverlayMaxWidth, OverlayMaxHeight);

            _overlayImage = _overlayObject.GetComponent<Image>();
            _overlayImage.raycastTarget = false;
            _overlayImage.preserveAspect = true;

            _overlayObject.SetActive(false);
        }

        private void OnDestroy()
        {
            OnDisable();

            if (_overlayObject != null)
            {
                Destroy(_overlayObject);
                _overlayObject = null;
                _overlayRect = null;
            }
        }

        //====================[ Sprite Loading ]====================

        private static void EnsureSpritesLoaded()
        {
            if (_spritesLoaded) return;
            _spritesLoaded = true;

            _bleedingOutSprite = LoadEmbeddedSprite("KeepMeAlive.Resources.bleeding_out.png", "bleeding_out");
            _revivingSprite = LoadEmbeddedSprite("KeepMeAlive.Resources.reviving.png", "reviving");

            if (_bleedingOutSprite == null)
                Plugin.LogSource.LogWarning("[FikaOverlay] bleeding_out.png embedded resource not found.");
            if (_revivingSprite == null)
                Plugin.LogSource.LogWarning("[FikaOverlay] reviving.png embedded resource not found.");
        }

        private static Sprite LoadEmbeddedSprite(string resourceName, string tag)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null) return null;

                byte[] bytes;
                using (var br = new System.IO.BinaryReader(stream))
                    bytes = br.ReadBytes((int)stream.Length);

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) return null;

                tex.name = $"KMA_Overlay_{tag}";
                return Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[FikaOverlay] LoadEmbeddedSprite({tag}) error: {ex.Message}");
                return null;
            }
        }
    }
}