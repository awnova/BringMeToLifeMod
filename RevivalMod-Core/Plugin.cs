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
