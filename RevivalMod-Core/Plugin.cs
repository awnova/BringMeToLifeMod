using BepInEx;
using BepInEx.Logging;
using System;
using RevivalMod.Features;
using BepInEx.Bootstrap;
using RevivalMod.Fika;
using RevivalMod.Patches;
using HarmonyLib;
using RevivalMod.Helpers;
using System.Reflection;
using RevivalMod.Components;
using UnityEngine;

namespace RevivalMod
{
    // first string below is your plugin's GUID, it MUST be unique to any other mod. Read more about it in BepInEx docs. Be sure to update it if you copy this project.
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("com.kobethuy.BringMeToLifeMod", "BringMeToLifeMod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        public static MonoBehaviour StaticCoroutineRunner;
        
        public static bool FikaInstalled { get; private set; }
        public static bool IAmDedicatedClient { get; private set; }

        // BaseUnityPlugin inherits MonoBehaviour, so you can use base unity functions like Awake() and Update()
        private void Awake()
        {
            FikaInstalled = Chainloader.PluginInfos.ContainsKey("com.fika.core");
            IAmDedicatedClient = Chainloader.PluginInfos.ContainsKey("com.fika.headless");

            // save the Logger to variable so we can use it elsewhere in the project
            LogSource = Logger;
            
            // Log assembly location & timestamp so we can identify which DLL was loaded at runtime
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

            // Set up coroutine runner for async cleanup operations
            StaticCoroutineRunner = this;
            LogSource.LogInfo("Revival plugin loaded!");
            RevivalModSettings.Init(Config);

            // Enable core revival system patches
            new RevivalFeatures().Enable();
            new OnPlayerCreatedPatch().Enable();
            new GameStartedPatch().Enable();
            new DeathPatch().Enable();
            new AvailableActionsPatch().Enable();
            
            // Enable new ghost mode system (enemy list manipulation)
            new GhostModeCriticalStatePatch().Enable();
            new GhostModeRevivalPatch().Enable();
            var deathPatch = new GhostModeDeathPatch();
            // Only enable the patch if its target exists on RevivalFeatures
            try
            {
                var deathTarget = AccessTools.Method(typeof(RevivalFeatures), "ForcePlayerDeath");
                if (deathTarget != null)
                {
                    deathPatch.Enable();
                }
                else
                {
                    LogSource.LogWarning("GhostModeDeathPatch not enabled because target method was not found");
                }
            }
            catch (Exception ex)
            {
                LogSource.LogError($"Error enabling GhostModeDeathPatch: {ex.Message}");
            }
        }

        private void OnEnable()
        {
            FikaBridge.PluginEnable();
        }
  
    }
}
