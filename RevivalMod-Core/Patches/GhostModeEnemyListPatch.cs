using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using RevivalMod.Helpers;
using RevivalMod.Features;
using UnityEngine;

namespace RevivalMod.Patches
{
    /// <summary>
    /// Ghost Mode Enemy List Manager
    /// Removes players from AI enemy lists during critical state for proper ghost mode functionality.
    /// This approach works with both vanilla AI and SAIN AI systems.
    /// </summary>
    public static class GhostModeEnemyManager
    {
        // Track which bots had the player as an enemy before ghost mode
        private static Dictionary<string, List<BotEnemiesController>> _playerEnemyLists = new();
        private static Dictionary<string, List<BotsGroup>> _playerGroupLists = new();
        
        // Track original enemy relationships for restoration
        private static Dictionary<string, Dictionary<BotEnemiesController, EnemyInfo>> _originalEnemyInfos = new();
        private static Dictionary<string, Dictionary<BotsGroup, BotSettingsClass>> _originalGroupEnemies = new();

        /// <summary>
        /// Remove player from all AI enemy lists (enter ghost mode)
        /// </summary>
        public static void EnterGhostMode(Player player)
        {
            if (!RevivalModSettings.PLAYER_ALIVE.Value)
                return;

            string playerId = player.ProfileId;
            
            try
            {
                Plugin.LogSource.LogInfo($"GhostMode: Entering ghost mode for {player.Profile.Nickname}");
                
                // Clear any previous state
                _playerEnemyLists[playerId] = new List<BotEnemiesController>();
                _playerGroupLists[playerId] = new List<BotsGroup>();
                _originalEnemyInfos[playerId] = new Dictionary<BotEnemiesController, EnemyInfo>();
                _originalGroupEnemies[playerId] = new Dictionary<BotsGroup, BotSettingsClass>();

                // Find all bot controllers and groups
                var allBots = FindAllBotControllers();
                var allGroups = FindAllBotGroups();

                // Remove from individual bot enemy lists
                foreach (var botController in allBots)
                {
                    if (botController.EnemyInfos.ContainsKey(player))
                    {
                        // Store original enemy info for restoration
                        _originalEnemyInfos[playerId][botController] = botController.EnemyInfos[player];
                        
                        // Remove from this bot's enemy list
                        botController.Remove(player);
                        _playerEnemyLists[playerId].Add(botController);
                        
                        Plugin.LogSource.LogInfo($"GhostMode: Removed {player.Profile.Nickname} from bot {botController.botOwner_0.Profile.Nickname}'s enemy list");
                    }
                }

                // Remove from group enemy lists
                foreach (var botGroup in allGroups)
                {
                    if (botGroup.Enemies.ContainsKey(player))
                    {
                        // Store original group settings for restoration
                        _originalGroupEnemies[playerId][botGroup] = botGroup.Enemies[player];
                        
                        // Remove from this group's enemy list
                        botGroup.RemoveEnemy(player, EBotEnemyCause.initial);
                        _playerGroupLists[playerId].Add(botGroup);
                        
                        Plugin.LogSource.LogInfo($"GhostMode: Removed {player.Profile.Nickname} from bot group enemy list");
                    }
                }

                Plugin.LogSource.LogInfo($"GhostMode: Successfully removed {player.Profile.Nickname} from {_playerEnemyLists[playerId].Count} bot lists and {_playerGroupLists[playerId].Count} group lists");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error entering ghost mode for {player.Profile.Nickname}: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-add player to all AI enemy lists (exit ghost mode)
        /// </summary>
        public static void ExitGhostMode(Player player)
        {
            if (!RevivalModSettings.PLAYER_ALIVE.Value)
                return;

            string playerId = player.ProfileId;
            
            try
            {
                Plugin.LogSource.LogInfo($"GhostMode: Exiting ghost mode for {player.Profile.Nickname}");

                // Re-add to individual bot enemy lists
                if (_originalEnemyInfos.ContainsKey(playerId))
                {
                    foreach (var kvp in _originalEnemyInfos[playerId])
                    {
                        var botController = kvp.Key;
                        var enemyInfo = kvp.Value;
                        
                        // Re-add to this bot's enemy list
                        botController.SetInfo(player, enemyInfo);
                        
                        Plugin.LogSource.LogInfo($"GhostMode: Re-added {player.Profile.Nickname} to bot {botController.botOwner_0.Profile.Nickname}'s enemy list");
                    }
                }

                // Re-add to group enemy lists
                if (_originalGroupEnemies.ContainsKey(playerId))
                {
                    foreach (var kvp in _originalGroupEnemies[playerId])
                    {
                        var botGroup = kvp.Key;
                        var groupSettings = kvp.Value;
                        
                        // Re-add to this group's enemy list
                        botGroup.AddEnemy(player, EBotEnemyCause.initial);
                        
                        Plugin.LogSource.LogInfo($"GhostMode: Re-added {player.Profile.Nickname} to bot group enemy list");
                    }
                }

                // Clean up tracking data
                CleanupPlayerData(playerId);
                
                Plugin.LogSource.LogInfo($"GhostMode: Successfully restored {player.Profile.Nickname} to enemy lists");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error exiting ghost mode for {player.Profile.Nickname}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean up tracking data for a player
        /// </summary>
        private static void CleanupPlayerData(string playerId)
        {
            _playerEnemyLists.Remove(playerId);
            _playerGroupLists.Remove(playerId);
            _originalEnemyInfos.Remove(playerId);
            _originalGroupEnemies.Remove(playerId);
        }

        /// <summary>
        /// Find all BotEnemiesController instances in the game
        /// </summary>
        private static List<BotEnemiesController> FindAllBotControllers()
        {
            var controllers = new List<BotEnemiesController>();
            
            try
            {
                // Get all BotOwner instances
                var allBots = UnityEngine.Object.FindObjectsOfType<BotOwner>();
                
                foreach (var bot in allBots)
                {
                    if (bot?.EnemiesController != null)
                    {
                        controllers.Add(bot.EnemiesController);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error finding bot controllers: {ex.Message}");
            }
            
            return controllers;
        }

        /// <summary>
        /// Find all BotsGroup instances in the game
        /// </summary>
        private static List<BotsGroup> FindAllBotGroups()
        {
            var groups = new List<BotsGroup>();
            
            try
            {
                // Get all BotOwner instances and collect their groups
                var allBots = UnityEngine.Object.FindObjectsOfType<BotOwner>();
                var groupSet = new HashSet<BotsGroup>();
                
                foreach (var bot in allBots)
                {
                    if (bot?.BotsGroup != null)
                    {
                        groupSet.Add(bot.BotsGroup);
                    }
                }
                
                groups.AddRange(groupSet);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error finding bot groups: {ex.Message}");
            }
            
            return groups;
        }

        /// <summary>
        /// Check if a player is currently in ghost mode
        /// </summary>
        public static bool IsPlayerInGhostMode(string playerId)
        {
            return _playerEnemyLists.ContainsKey(playerId) && _playerEnemyLists[playerId].Count > 0;
        }

        /// <summary>
        /// Enter ghost mode by player id. Resolves player instance if present, otherwise caller should ensure RMSession stores the id.
        /// This method is safe to call from packet handlers where the Player object may not yet exist locally.
        /// </summary>
        public static void EnterGhostModeById(string playerId)
        {
            try
            {
                // Try resolve Player instance
                Player player = null;
                try
                {
                    player = Singleton<GameWorld>.Instance.GetEverExistedPlayerByID(playerId);
                }
                catch { /* ignore resolve errors */ }

                if (player != null)
                {
                    // If player exists locally, reuse the existing logic
                    EnterGhostMode(player);
                    return;
                }

                // No local Player instance: record that this id is in ghost mode by creating empty tracking collections
                // so that when the player spawns later we can apply restoration/cleanup properly.
                if (!_playerEnemyLists.ContainsKey(playerId))
                {
                    _playerEnemyLists[playerId] = new List<BotEnemiesController>();
                }
                if (!_playerGroupLists.ContainsKey(playerId))
                {
                    _playerGroupLists[playerId] = new List<BotsGroup>();
                }
                if (!_originalEnemyInfos.ContainsKey(playerId))
                {
                    _originalEnemyInfos[playerId] = new Dictionary<BotEnemiesController, EnemyInfo>();
                }
                if (!_originalGroupEnemies.ContainsKey(playerId))
                {
                    _originalGroupEnemies[playerId] = new Dictionary<BotsGroup, BotSettingsClass>();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error in EnterGhostModeById for {playerId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Exit ghost mode by player id. Resolves player instance if present, otherwise cleans up tracking data.
        /// </summary>
        public static void ExitGhostModeById(string playerId)
        {
            try
            {
                Player player = null;
                try
                {
                    player = Singleton<GameWorld>.Instance.GetEverExistedPlayerByID(playerId);
                }
                catch { }

                if (player != null)
                {
                    ExitGhostMode(player);
                    return;
                }

                // If no local player instance, just cleanup any tracking placeholders
                CleanupPlayerData(playerId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error in ExitGhostModeById for {playerId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch to trigger ghost mode when player enters critical state
    /// </summary>
    internal class GhostModeCriticalStatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Patch the RevivalFeatures method that handles critical state
            return AccessTools.Method(typeof(RevivalFeatures), "SetPlayerCriticalState");
        }

        [PatchPostfix]
        private static void PatchPostfix(Player player, bool criticalState)
        {
            try
            {
                if (!RevivalModSettings.PLAYER_ALIVE.Value)
                    return;

                if (player == null || !player.IsYourPlayer)
                    return;

                if (criticalState)
                {
                    // Enter ghost mode (use id-based helper so local and remote flows match)
                    GhostModeEnemyManager.EnterGhostModeById(player.ProfileId);
                }
                else
                {
                    // Exit ghost mode (use id-based helper so local and remote flows match)
                    GhostModeEnemyManager.ExitGhostModeById(player.ProfileId);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error in critical state patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch to trigger ghost mode when player is revived
    /// </summary>
    internal class GhostModeRevivalPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Patch the RevivalFeatures method that handles revival
            return AccessTools.Method(typeof(RevivalFeatures), "TryPerformManualRevival");
        }

        [PatchPostfix]
        private static void PatchPostfix(Player player)
        {
            try
            {
                if (!RevivalModSettings.PLAYER_ALIVE.Value)
                    return;

                if (player == null || !player.IsYourPlayer)
                    return;

                // Exit ghost mode when revived
                GhostModeEnemyManager.ExitGhostMode(player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error in revival patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch to trigger ghost mode when player dies (give up)
    /// </summary>
    internal class GhostModeDeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Patch the RevivalFeatures method that handles death
            return AccessTools.Method(typeof(RevivalFeatures), "KillPlayer");
        }

        [PatchPostfix]
        private static void PatchPostfix(Player player)
        {
            try
            {
                if (!RevivalModSettings.PLAYER_ALIVE.Value)
                    return;

                if (player == null || !player.IsYourPlayer)
                    return;

                // Exit ghost mode when player dies
                GhostModeEnemyManager.ExitGhostMode(player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode: Error in death patch: {ex.Message}");
            }
        }
    }
}
