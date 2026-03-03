using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
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
    [BepInPlugin("com.KeepMeAlive", "KeepMeAlive", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static MonoBehaviour StaticCoroutineRunner;

        public static bool IAmDedicatedClient { get; private set; }
        public static bool SAINInstalled { get; private set; }

        private void Awake()
        {
            IAmDedicatedClient = Chainloader.PluginInfos.ContainsKey("com.fika.headless");
            SAINInstalled = Chainloader.PluginInfos.ContainsKey("me.sol.sain");

            LogSource = Logger;
            StaticCoroutineRunner = this;

            LogAssemblyInfo();
            LogSource.LogInfo("Revival plugin loaded!");

            RevivalModSettings.Init(Config);
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

            new FikaHealthSyncLocalPlayerGuardPatch().Enable();
            new HandleProceedPatch().Enable();

            new TeamHealGEventArgs13SuppressPatch().Enable();
            new TeamHealRemoveItemSuppressPatch().Enable();
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
    }
}
