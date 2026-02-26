using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using RevivalMod.Features;
using RevivalMod.Fika;
using RevivalMod.Helpers;
using RevivalMod.Patches;
using System;
using System.Reflection;
using UnityEngine;

namespace RevivalMod
{
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("com.kobethuy.BringMeToLifeMod", "BringMeToLifeMod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static MonoBehaviour StaticCoroutineRunner;

        public static bool FikaInstalled { get; private set; }
        public static bool IAmDedicatedClient { get; private set; }

        private void Awake()
        {
            FikaInstalled = Chainloader.PluginInfos.ContainsKey("com.fika.core");
            IAmDedicatedClient = Chainloader.PluginInfos.ContainsKey("com.fika.headless");

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
        }

        private static void EnableGhostModePatches()
        {
            try
            {
                new GhostModeAddEnemyPatch().Enable();
                LogSource.LogInfo("GhostModeAddEnemyPatch enabled (blocks BotsGroup.AddEnemy for ghosted players).");
            }
            catch (Exception ex)
            {
                LogSource.LogError($"Error enabling GhostModeAddEnemyPatch: {ex.Message}");
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
