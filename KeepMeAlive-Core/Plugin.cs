//====================[ Imports ]====================
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Fika.Core.Main.Utils;
using KeepMeAlive.Features;
using KeepMeAlive.Fika;
using KeepMeAlive.Helpers;
using KeepMeAlive.Patches;
using System;
using System.Reflection;
using UnityEngine;

namespace KeepMeAlive
{
    //====================[ Plugin ]====================
    [BepInDependency("com.fika.core")]
    [BepInDependency("com.fika.headless", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("com.KeepMeAlive", "KeepMeAlive", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        //====================[ Shared State ]====================
        public static ManualLogSource LogSource;
        public static MonoBehaviour StaticCoroutineRunner;

        /// <summary>Pass-through to the canonical Fika flag set by FikaHeadlessPlugin.Awake().</summary>
        public static bool IAmDedicatedClient => FikaBackendUtils.IsHeadless;
        public static bool SAINInstalled { get; private set; }

        //====================[ Unity Lifecycle ]====================
        private void Awake()
        {
            SAINInstalled = Chainloader.PluginInfos.ContainsKey("me.sol.sain");

            LogSource = Logger;
            StaticCoroutineRunner = this;

            LogAssemblyInfo();
            LogSource.LogInfo("Revival plugin loaded!");

            KeepMeAliveSettings.Init(Config);

            // Register ReviveItemCooldown effect types into EFT's reflection-based type registries.
            // Must run before any health controllers are instantiated.
            RegisterReviveItemCooldownEffectTypes();

            EnableCorePatches();
            EnableGhostModePatches();
        }

        private void OnEnable() => FikaBridge.PluginEnable();

        //====================[ Patch Registration ]====================
        private static void EnableCorePatches()
        {
            new RevivalFeatures().Enable();
            new OnPlayerCreatedPatch().Enable();
            new GameStartedPatch().Enable();
            new DeathPatch().Enable();
            new DownedWeaponProceedBlockPatch().Enable();
            new DownedClientWeaponProceedBlockPatch().Enable();
            new DownedFikaWeaponProceedBlockPatch().Enable();
            new AvailableActionsPatch().Enable();
            new SpecialSlotReviveItemPatch().Enable();
            new ReviveItemCooldownIconPatch().Enable();
            new FikaOverlayPatch().Enable();
            new InventoryScreenInputBlockPatch().Enable();
            new SilentInventoryCommandBlockPatch().Enable();
        }

        private static void EnableGhostModePatches()
        {
            new GhostModeGroupPatch().Enable();
            new GhostModeMemoryPatch().Enable();
            LogSource.LogInfo("GhostMode patches enabled (BotsGroup.AddEnemy + BotMemoryClass.AddEnemy).");

            if (SAINInstalled)
            {
                new GhostModeSAINPatch().Enable();
                LogSource.LogInfo("GhostMode SAIN patch enabled (EnemyListController.tryAddEnemy).");
            }
        }

        //====================[ Diagnostics ]====================
        private static void LogAssemblyInfo()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                LogSource.LogInfo($"Revival assembly: {asm.GetName().Name} v{asm.GetName().Version}");
            }
            catch (Exception ex)
            {
                LogSource.LogWarning($"Could not retrieve assembly info: {ex.Message}");
            }
        }

        //====================[ ReviveItemCooldown Type Registration ]====================
        // Registers ReviveItemCooldown effect types into EFT's three reflection-based registries
        // so the effect can be serialized/deserialized over Fika's health sync network.
        private static void RegisterReviveItemCooldownEffectTypes()
        {
            try
            {
                LogSource.LogInfo("[Plugin] Registering ReviveItemCooldown effect types...");
                RegisterEffectSenderType(typeof(ReviveItemCooldownEffect));
                RegisterEffectReceiverType(senderType: typeof(ReviveItemCooldownEffect), receiverType: typeof(ReviveItemCooldownNetworkEffect));
                GClass3058.Dictionary_1[typeof(IReviveItemCooldown)] = "ReviveItemCooldown";

                // Verify
                var dict0 = (Dictionary<string, byte>)AccessTools.Field(typeof(GClass3058.GClass3059), "Dictionary_0").GetValue(null);
                var closedType = typeof(GClass3058.GClass3060<>).MakeGenericType(typeof(NetworkHealthControllerAbstractClass));
                var recvDict = (Dictionary<string, Func<object>>)AccessTools.Field(closedType, "Dictionary_0").GetValue(null);
                LogSource.LogInfo(
                    $"[Plugin] ReviveItemCooldown registered Ã¢â‚¬â€ sender: {dict0.ContainsKey(nameof(ReviveItemCooldownEffect))}, " +
                    $"receiver: {recvDict.ContainsKey(nameof(ReviveItemCooldownEffect))}, " +
                    $"interface: {GClass3058.Dictionary_1.ContainsKey(typeof(IReviveItemCooldown))}");
            }
            catch (Exception ex)
            {
                LogSource.LogError($"[Plugin] ReviveItemCooldown type registration failed: {ex}");
            }
        }

        // GClass3059: Don't modify Type_0 (it contains only nested types from ActiveHealthController).
        // Only add to the dictionaries Ã¢â‚¬â€ receiver side has explicit factories via RegisterEffectReceiverType.
        private static void RegisterEffectSenderType(Type effectType)
        {
            var type3059   = typeof(GClass3058.GClass3059);
            var dict0Field = AccessTools.Field(type3059, "Dictionary_0");
            var dict1Field = AccessTools.Field(type3059, "Dictionary_1");

            var dict0 = (Dictionary<string, byte>)dict0Field.GetValue(null);
            var dict1 = (Dictionary<byte, string>)dict1Field.GetValue(null);

            if (dict0.ContainsKey(effectType.Name))
                return; // already registered

            // Assign a new byte that doesn't collide with existing vanilla types.
            // Start after the highest vanilla byte index.
            byte newIndex = (byte)dict1.Keys.Max();
            newIndex++;

            dict0[effectType.Name] = newIndex;
            dict1[newIndex] = effectType.Name;
        }

        // GClass3060<NetworkHealthControllerAbstractClass>.Dictionary_0: name Ã¢â€ â€™ factory.
        // Key must be the SENDER type's name because GClass3059 resolves byteÃ¢â€ â€™senderName,
        // then GClass3060 looks up that name to instantiate the receiver.
        private static void RegisterEffectReceiverType(Type senderType, Type receiverType)
        {
            var closedType = typeof(GClass3058.GClass3060<>).MakeGenericType(typeof(NetworkHealthControllerAbstractClass));
            var dict0 = (Dictionary<string, Func<object>>)AccessTools.Field(closedType, "Dictionary_0").GetValue(null);
            dict0[senderType.Name] = () => Activator.CreateInstance(receiverType);
        }
    }
}
