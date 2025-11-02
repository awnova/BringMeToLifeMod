//====================[ Imports ]====================
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using RevivalMod.Features;
using RevivalMod.Fika;
using RevivalMod.Helpers;
using RevivalMod.Patches;
using System;
using System.Reflection;
using UnityEngine;

namespace RevivalMod
{
    //====================[ Plugin ]====================
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("com.kobethuy.BringMeToLifeMod", "BringMeToLifeMod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        //====================[ Statics ]====================
        public static ManualLogSource LogSource;
        public static MonoBehaviour   StaticCoroutineRunner;

        public static bool FikaInstalled      { get; private set; }
        public static bool IAmDedicatedClient { get; private set; }

        //====================[ Lifecycle ]====================
        private void Awake()
        {
            FikaInstalled      = Chainloader.PluginInfos.ContainsKey("com.fika.core");
            IAmDedicatedClient = Chainloader.PluginInfos.ContainsKey("com.fika.headless");

            LogSource = Logger;
            LogAssemblyInfo();

            StaticCoroutineRunner = this;
            LogSource.LogInfo("Revival plugin loaded!");

            RevivalModSettings.Init(Config);

            EnableCorePatches();
            EnableGhostModePatches();
        }

        private void OnEnable() => FikaBridge.PluginEnable();

        //====================[ Helpers ]====================
        private static void EnableCorePatches()
        {
            new RevivalFeatures().Enable();
            new OnPlayerCreatedPatch().Enable();
            new GameStartedPatch().Enable();
            new DeathPatch().Enable();
            new AvailableActionsPatch().Enable();
        }

        private static void EnableGhostModePatches()
        {
            new GhostModeCriticalStatePatch().Enable();
            new GhostModeRevivalPatch().Enable();

            try
            {
                // Only enable GhostModeDeathPatch if target exists
                var deathTarget = AccessTools.Method(typeof(RevivalFeatures), "ForcePlayerDeath");
                if (deathTarget != null) new GhostModeDeathPatch().Enable();
                else LogSource.LogWarning("GhostModeDeathPatch not enabled: target method not found");
            }
            catch (Exception ex)
            {
                LogSource.LogError($"Error enabling GhostModeDeathPatch: {ex.Message}");
            }
        }

        private static void LogAssemblyInfo()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                LogSource.LogInfo($"Revival assembly: {asm.GetName().Name} v{asm.GetName().Version}");
                LogSource.LogInfo($"Revival assembly location: {asm.Location}");

                try
                {
                    var fi = new System.IO.FileInfo(asm.Location);
                    LogSource.LogInfo($"Revival assembly last write (UTC): {fi.LastWriteTimeUtc:o}");
                }
                catch (Exception ex)
                {
                    LogSource.LogWarning($"Could not read assembly file info: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LogSource.LogWarning($"Could not retrieve assembly info: {ex.Message}");
            }
        }
    }
}
