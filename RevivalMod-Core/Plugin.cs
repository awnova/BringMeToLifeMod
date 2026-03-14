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
    [BepInDependency("com.fika.core")]
    [BepInDependency("com.fika.headless", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("com.KeepMeAlive", "KeepMeAlive", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static MonoBehaviour StaticCoroutineRunner;

        /// <summary>Pass-through to the canonical Fika flag set by FikaHeadlessPlugin.Awake().</summary>
        public static bool IAmDedicatedClient => FikaBackendUtils.IsHeadless;
        public static bool SAINInstalled { get; private set; }

        private void Awake()
        {
            SAINInstalled = Chainloader.PluginInfos.ContainsKey("me.sol.sain");

            LogSource = Logger;
            StaticCoroutineRunner = this;

            LogAssemblyInfo();
            LogSource.LogInfo("Revival plugin loaded!");

            RevivalModSettings.Init(Config);

            // Register DefibCooldown effect types into EFT's reflection-based type registries.
            // Must run before any health controllers are instantiated.
            RegisterDefibCooldownEffectTypes();

            EnableCorePatches();
            EnableGhostModePatches();
        }

        private void OnEnable() => FikaBridge.PluginEnable();

        private static void EnableCorePatches()
        {
            new RevivalFeatures().Enable();
            new OnPlayerCreatedPatch().Enable();
            new GameStartedPatch().Enable();
            new DeathPatch().Enable();
            new AvailableActionsPatch().Enable();
            new SpecialSlotDefibPatch().Enable();
            new DefibCooldownIconPatch().Enable();
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

        //====================[ DefibCooldown Type Registration ]====================
        // Registers DefibCooldown effect types into EFT's three reflection-based registries
        // so the effect can be serialized/deserialized over Fika's health sync network.
        private static void RegisterDefibCooldownEffectTypes()
        {
            try
            {
                LogSource.LogInfo("[Plugin] Registering DefibCooldown effect types...");
                RegisterEffectSenderType(typeof(DefibCooldownEffect));
                RegisterEffectReceiverType(senderType: typeof(DefibCooldownEffect), receiverType: typeof(DefibCooldownNetworkEffect));
                GClass3058.Dictionary_1[typeof(IDefibCooldown)] = "DefibCooldown";

                // Verify
                var dict0 = (Dictionary<string, byte>)AccessTools.Field(typeof(GClass3058.GClass3059), "Dictionary_0").GetValue(null);
                var closedType = typeof(GClass3058.GClass3060<>).MakeGenericType(typeof(NetworkHealthControllerAbstractClass));
                var recvDict = (Dictionary<string, Func<object>>)AccessTools.Field(closedType, "Dictionary_0").GetValue(null);
                LogSource.LogInfo(
                    $"[Plugin] DefibCooldown registered — sender: {dict0.ContainsKey(nameof(DefibCooldownEffect))}, " +
                    $"receiver: {recvDict.ContainsKey(nameof(DefibCooldownEffect))}, " +
                    $"interface: {GClass3058.Dictionary_1.ContainsKey(typeof(IDefibCooldown))}");
            }
            catch (Exception ex)
            {
                LogSource.LogError($"[Plugin] DefibCooldown type registration failed: {ex}");
            }
        }

        // GClass3059: sorted Type[] + two name↔byte dicts. Insert our type, re-sort, rebuild.
        private static void RegisterEffectSenderType(Type effectType)
        {
            var type3059    = typeof(GClass3058.GClass3059);
            var typeArrayField = AccessTools.Field(type3059, "Type_0");
            var dict0Field     = AccessTools.Field(type3059, "Dictionary_0");
            var dict1Field     = AccessTools.Field(type3059, "Dictionary_1");

            var current  = (Type[])typeArrayField.GetValue(null);
            var newArray = current.Concat(new[] { effectType }).OrderBy(t => t.Name).ToArray();
            var dict0    = newArray.ToDictionary(t => t.Name, t => (byte)Array.IndexOf(newArray, t));
            var dict1    = dict0.ToDictionary(kv => kv.Value, kv => kv.Key);

            typeArrayField.SetValue(null, newArray);
            dict0Field.SetValue(null, dict0);
            dict1Field.SetValue(null, dict1);
        }

        // GClass3060<NetworkHealthControllerAbstractClass>.Dictionary_0: name → factory.
        // Key must be the SENDER type's name because GClass3059 resolves byte→senderName,
        // then GClass3060 looks up that name to instantiate the receiver.
        private static void RegisterEffectReceiverType(Type senderType, Type receiverType)
        {
            var closedType = typeof(GClass3058.GClass3060<>).MakeGenericType(typeof(NetworkHealthControllerAbstractClass));
            var dict0 = (Dictionary<string, Func<object>>)AccessTools.Field(closedType, "Dictionary_0").GetValue(null);
            dict0[senderType.Name] = () => Activator.CreateInstance(receiverType);
        }
    }
}
