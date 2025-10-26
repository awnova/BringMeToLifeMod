//====================[ Imports ]====================
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using Comfort.Common;
using System;
using System.Collections.Generic;
using System.Reflection;
using RevivalMod.Helpers;
using RevivalMod.Features;
using UnityEngine;

namespace RevivalMod.Patches
{
    //====================[ GhostModeEnemyManager ]====================
    // Removes players from AI enemy lists during critical state for proper ghost mode functionality.
    // This approach works with both vanilla AI and SAIN AI systems.
    public static class GhostModeEnemyManager
    {
        //====================[ Fields ]====================
        // Tracks which bots or groups had the player as an enemy before ghost mode
        private static Dictionary<string, List<BotEnemiesController>> _playerEnemyLists = new();
        private static Dictionary<string, List<BotsGroup>> _playerGroupLists = new();

        // Stores the original enemy data for later restoration
        private static Dictionary<string, Dictionary<BotEnemiesController, EnemyInfo>> _originalEnemyInfos = new();
        private static Dictionary<string, Dictionary<BotsGroup, BotSettingsClass>> _originalGroupEnemies = new();

        //====================[ Enter Ghost Mode (Player Instance) ]====================
        public static void EnterGhostMode(Player player)
        {
            string playerId = player.ProfileId;

            try
            {
                Plugin.LogSource.LogInfo($"GhostMode: Entering ghost mode for {player.Profile.Nickname}");

                // Reset tracking data
                _playerEnemyLists[playerId] = new List<BotEnemiesController>();
                _playerGroupLists[playerId] = new List<BotsGroup>();
                _originalEnemyInfos[playerId] = new Dictionary<BotEnemiesController, EnemyInfo>();
                _originalGroupEnemies[playerId] = new Dictionary<BotsGroup, BotSettingsClass>();

                // Collect all known AI bots and groups
                var allBots = FindAllBotControllers();
                var allGroups = FindAllBotGroups();

                // Remove the player from each bot’s enemy list
                foreach (var botController in allBots)
                {
                    if (botController.EnemyInfos.ContainsKey(player))
                    {
                        _originalEnemyInfos[playerId][botController] = botController.EnemyInfos[player];
                        botController.Remove(player);
                        _playerEnemyLists[playerId].Add(botController);

                        Plugin.LogSource.LogInfo($"GhostMode: Removed {player.Profile.Nickname} from bot {botController.botOwner_0.Profile.Nickname}'s enemy list");
                    }
                }

                // Remove the player from each group’s enemy list
                foreach (var botGroup in allGroups)
                {
                    if (botGroup.Enemies.ContainsKey(player))
                    {
                        _originalGroupEnemies[playerId][botGroup] = botGroup.Enemies[player];
                        botGroup.RemoveEnemy(player, EBotEnemyCause.initial);
                        _playerGroupLists[playerId].Add(botGroup);

                        Plugin.LogSource.LogInfo($"GhostMode: Removed {player.Profile.Nickname} from bot group enemy list");
                    }
                }

                Plugin.LogSource.LogInfo($"GhostMode: Finished removing {player.Profile.Nickname} from {_playerEnemyLists[playerId].Count} bot lists and {_playerGroupLists[playerId].Count} group lists");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error entering ghost mode for {player.Profile.Nickname}: {ex.Message}");
            }
        }

        //====================[ Enter Ghost Mode (By ID) ]====================
        // Used by network packets or remote players when Player object may not exist locally
        public static void EnterGhostModeById(string playerId)
        {
            try
            {
                Player player = null;
                try { player = Singleton<GameWorld>.Instance.GetEverExistedPlayerByID(playerId); } catch { }

                // If player exists locally, use main logic
                if (player != null)
                {
                    EnterGhostMode(player);
                    return;
                }

                // Otherwise, create placeholder tracking structures for later restoration
                if (!_playerEnemyLists.ContainsKey(playerId)) _playerEnemyLists[playerId] = new List<BotEnemiesController>();
                if (!_playerGroupLists.ContainsKey(playerId)) _playerGroupLists[playerId] = new List<BotsGroup>();
                if (!_originalEnemyInfos.ContainsKey(playerId)) _originalEnemyInfos[playerId] = new Dictionary<BotEnemiesController, EnemyInfo>();
                if (!_originalGroupEnemies.ContainsKey(playerId)) _originalGroupEnemies[playerId] = new Dictionary<BotsGroup, BotSettingsClass>();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error in EnterGhostModeById for {playerId}: {ex.Message}");
            }
        }

        //====================[ Exit Ghost Mode (Player Instance) ]====================
        public static void ExitGhostMode(Player player)
        {
            string playerId = player.ProfileId;

            try
            {
                Plugin.LogSource.LogInfo($"GhostMode: Exiting ghost mode for {player.Profile.Nickname}");

                // Restore individual bot enemies
                if (_originalEnemyInfos.ContainsKey(playerId))
                {
                    foreach (var kvp in _originalEnemyInfos[playerId])
                    {
                        kvp.Key.SetInfo(player, kvp.Value);
                        Plugin.LogSource.LogInfo($"GhostMode: Restored {player.Profile.Nickname} to bot {kvp.Key.botOwner_0.Profile.Nickname}'s enemy list");
                    }
                }

                // Restore group enemies
                if (_originalGroupEnemies.ContainsKey(playerId))
                {
                    foreach (var kvp in _originalGroupEnemies[playerId])
                    {
                        kvp.Key.AddEnemy(player, EBotEnemyCause.initial);
                        Plugin.LogSource.LogInfo($"GhostMode: Restored {player.Profile.Nickname} to bot group enemy list");
                    }
                }

                // Clean up tracking data
                CleanupPlayerData(playerId);
                Plugin.LogSource.LogInfo($"GhostMode: Successfully re-added {player.Profile.Nickname} to all tracked enemies");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error exiting ghost mode for {player.Profile.Nickname}: {ex.Message}");
            }
        }

        //====================[ Exit Ghost Mode (By ID) ]====================
        // Called when the Player object may not exist (e.g. remote clients or cleanup)
        public static void ExitGhostModeById(string playerId)
        {
            try
            {
                Player player = null;
                try { player = Singleton<GameWorld>.Instance.GetEverExistedPlayerByID(playerId); } catch { }

                if (player != null)
                {
                    ExitGhostMode(player);
                    return;
                }

                // Fallback: clean tracking data only
                CleanupPlayerData(playerId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error in ExitGhostModeById for {playerId}: {ex.Message}");
            }
        }

        //====================[ Utilities ]====================
        // Removes all tracking data for a player (called after exit or cleanup)
        private static void CleanupPlayerData(string playerId)
        {
            _playerEnemyLists.Remove(playerId);
            _playerGroupLists.Remove(playerId);
            _originalEnemyInfos.Remove(playerId);
            _originalGroupEnemies.Remove(playerId);
        }

        // Quick check to determine if a player is currently in ghost mode
        public static bool IsPlayerInGhostMode(string playerId)
        {
            return _playerEnemyLists.ContainsKey(playerId) && _playerEnemyLists[playerId].Count > 0;
        }

        //====================[ Lookup Helpers ]====================
        // Finds all active BotEnemiesController instances
        private static List<BotEnemiesController> FindAllBotControllers()
        {
            var controllers = new List<BotEnemiesController>();

            try
            {
                var allBots = UnityEngine.Object.FindObjectsOfType<BotOwner>();
                foreach (var bot in allBots)
                {
                    if (bot?.EnemiesController != null)
                        controllers.Add(bot.EnemiesController);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error finding bot controllers: {ex.Message}");
            }

            return controllers;
        }

        // Finds all unique BotsGroup instances currently active
        private static List<BotsGroup> FindAllBotGroups()
        {
            var groups = new List<BotsGroup>();

            try
            {
                var allBots = UnityEngine.Object.FindObjectsOfType<BotOwner>();
                var groupSet = new HashSet<BotsGroup>();

                foreach (var bot in allBots)
                {
                    if (bot?.BotsGroup != null)
                        groupSet.Add(bot.BotsGroup);
                }

                groups.AddRange(groupSet);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error finding bot groups: {ex.Message}");
            }

            return groups;
        }
    }

    //====================[ Patch: Critical State ]====================
    internal class GhostModeCriticalStatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Hooks into RevivalFeatures.SetPlayerCriticalState
            return AccessTools.Method(typeof(RevivalFeatures), "SetPlayerCriticalState");
        }

        [PatchPostfix]
        private static void PatchPostfix(Player player, bool criticalState)
        {
            try
            {
                if (player == null || !player.IsYourPlayer)
                    return;

                // Toggle ghost mode based on critical state flag
                if (criticalState)
                    GhostModeEnemyManager.EnterGhostModeById(player.ProfileId);
                else
                    GhostModeEnemyManager.ExitGhostModeById(player.ProfileId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error in critical state patch: {ex.Message}");
            }
        }
    }

    //====================[ Patch: Revival Event ]====================
    internal class GhostModeRevivalPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Hooks into RevivalFeatures.TryPerformManualRevival
            return AccessTools.Method(typeof(RevivalFeatures), "TryPerformManualRevival");
        }

        [PatchPostfix]
        private static void PatchPostfix(Player player)
        {
            try
            {
                if (player == null || !player.IsYourPlayer)
                    return;

                // Leave ghost mode when revived
                GhostModeEnemyManager.ExitGhostMode(player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error in revival patch: {ex.Message}");
            }
        }
    }

    //====================[ Patch: Death Event ]====================
    internal class GhostModeDeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Hooks into RevivalFeatures.ForcePlayerDeath (warn if renamed or missing)
            var method = AccessTools.Method(typeof(RevivalFeatures), "ForcePlayerDeath");
            if (method == null)
            {
                try { Plugin.LogSource.LogError("GhostModeDeathPatch: Target method 'ForcePlayerDeath' not found on RevivalFeatures"); } catch { }
            }
            return method;
        }

        [PatchPostfix]
        private static void PatchPostfix(Player player)
        {
            try
            {
                if (player == null || !player.IsYourPlayer)
                    return;

                // Leave ghost mode when player dies
                GhostModeEnemyManager.ExitGhostMode(player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error in death patch: {ex.Message}");
            }
        }
    }
}
