﻿using BepInEx;
using BepInEx.Logging;
using System;
using RevivalMod.Features;
using BepInEx.Bootstrap;
using RevivalMod.Fika;
using RevivalMod.Patches;
using RevivalMod.Helpers;
using System.Reflection;
using RevivalMod.Components;

namespace RevivalMod
{
    // first string below is your plugin's GUID, it MUST be unique to any other mod. Read more about it in BepInEx docs. Be sure to update it if you copy this project.
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("com.kaikinoodles.revivalmod", "RevivalMod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LogSource;
        
        public static bool FikaInstalled { get; private set; }
        public static bool IAmDedicatedClient { get; private set; }
        public const string DataToServerURL = "/kaikinoodles/revivalmod/data_to_server";
        public const string DataToClientURL = "/kaikinoodles/revivalmod/data_to_client";
        public static RevivalModPlayer revivalModPlayer;

        // BaseUnityPlugin inherits MonoBehaviour, so you can use base unity functions like Awake() and Update()
        private void Awake()
        {
            FikaInstalled = Chainloader.PluginInfos.ContainsKey("com.fika.core");
            IAmDedicatedClient = Chainloader.PluginInfos.ContainsKey("com.fika.headless");

            // save the Logger to variable so we can use it elsewhere in the project
            LogSource = Logger;
            LogSource.LogInfo("Revival plugin loaded!");
            RevivalModSettings.Init(Config);

            // Enable patches
            new DeathPatch().Enable();
            new RevivalFeatures().Enable();
            new GameStartedPatch().Enable();
            new OnPlayerCreatedPatch().Enable();
            new AvailableActionsPatch().Enable();

            LogSource.LogInfo("Revival plugin initialized! Press F5 to use your defibrillator when in critical state.");
            TryInitFikaAssembly();
        }

        private void OnEnable()
        {
            FikaBridge.PluginEnable();
        }

        void TryInitFikaAssembly()
        {
            if (!FikaInstalled) return;

            try
            {
                Assembly fikaModuleAssembly = Assembly.Load("RevivalMod-Fika");

                Type main = fikaModuleAssembly.GetType("RevivalMod.FikaModule.Main");
                MethodInfo init = main.GetMethod("Init");

                init.Invoke(main, null);
            }
            catch (Exception ex)
            {
                LogSource.LogError($"Error loading Fika assembly: {ex.Message}");
            }
        }

  
    }
}
