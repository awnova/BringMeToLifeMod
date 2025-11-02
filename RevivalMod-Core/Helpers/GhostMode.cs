//====================[ Imports ]====================
using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace RevivalMod.Helpers
{
    //====================[ GhostMode ]====================
    // Removes a player from AI enemy lists while downed/critical, then restores on exit.
    // Works with vanilla AI (and SAIN via shared enemy list APIs).
    public static class GhostMode
    {
        //====================[ State ]====================
        // Per-player tracking of which bots/groups we modified, plus original enemy info.
        private static readonly Dictionary<string, List<BotEnemiesController>> _playerEnemyLists = new();
        private static readonly Dictionary<string, List<BotsGroup>>            _playerGroupLists = new();

        private static readonly Dictionary<string, Dictionary<BotEnemiesController, EnemyInfo>> _originalEnemyInfos = new();
        private static readonly Dictionary<string, HashSet<BotsGroup>>                          _originalGroupSets = new();

        //====================[ Enter (Player) ]====================
        public static void EnterGhostMode(Player player)
        {
            if (player == null) return;
            string playerId = player.ProfileId;

            try
            {
                Plugin.LogSource.LogInfo($"[GhostMode] Enter for {player.Profile.Nickname}");

                ResetBuckets(playerId);

                // Discover active AI
                var allBots   = FindAllBotControllers();
                var allGroups = FindAllBotGroups();

                // Remove player from each bot's enemy list, saving original info
                foreach (var ec in allBots)
                {
                    if (ec == null) continue;
                    if (!ec.EnemyInfos.ContainsKey(player)) continue;

                    _originalEnemyInfos[playerId][ec] = ec.EnemyInfos[player];
                    ec.Remove(player); // removes enemy + threat
                    _playerEnemyLists[playerId].Add(ec);

                    Plugin.LogSource.LogDebug($"[GhostMode] -rm- bot:{ec.botOwner_0?.Profile?.Nickname}");
                }

                // Remove player from each group's enemy list (track group to restore later)
                foreach (var g in allGroups)
                {
                    if (g == null || !g.Enemies.ContainsKey(player)) continue;

                    _originalGroupSets[playerId].Add(g);
                    g.RemoveEnemy(player, EBotEnemyCause.initial);
                    _playerGroupLists[playerId].Add(g);

                    Plugin.LogSource.LogDebug("[GhostMode] -rm- group enemy");
                }

                Plugin.LogSource.LogInfo($"[GhostMode] Done: bots={_playerEnemyLists[playerId].Count}, groups={_playerGroupLists[playerId].Count}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] Enter error for {player.Profile.Nickname}: {ex.Message}");
            }
        }

        //====================[ Enter (by Id) ]====================
        // For remote/network calls where Player may not be present locally.
        public static void EnterGhostModeById(string playerId)
        {
            try
            {
                var p = Utils.GetPlayerById(playerId);
                if (p != null) { EnterGhostMode(p); return; }

                // No local Player => just ensure buckets so exit can safely restore/clear.
                ResetBuckets(playerId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] EnterById({playerId}) error: {ex.Message}");
            }
        }

        //====================[ Exit (Player) ]====================
        public static void ExitGhostMode(Player player)
        {
            if (player == null) return;
            string playerId = player.ProfileId;

            try
            {
                Plugin.LogSource.LogInfo($"[GhostMode] Exit for {player.Profile.Nickname}");

                // Restore per-bot enemy info
                if (_originalEnemyInfos.TryGetValue(playerId, out var map))
                {
                    foreach (var kv in map)
                    {
                        var ec = kv.Key; if (ec == null) continue;
                        ec.SetInfo(player, kv.Value);
                        Plugin.LogSource.LogDebug($"[GhostMode] -add- bot:{ec.botOwner_0?.Profile?.Nickname}");
                    }
                }

                // Restore group enemies (no extra data needed to re-add)
                if (_originalGroupSets.TryGetValue(playerId, out var set))
                {
                    foreach (var g in set)
                    {
                        if (g == null) continue;
                        g.AddEnemy(player, EBotEnemyCause.initial);
                        Plugin.LogSource.LogDebug("[GhostMode] -add- group enemy");
                    }
                }

                CleanupPlayerData(playerId);
                Plugin.LogSource.LogInfo($"[GhostMode] Exit ok for {player.Profile.Nickname}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] Exit error for {player.Profile.Nickname}: {ex.Message}");
            }
        }

        //====================[ Exit (by Id) ]====================
        public static void ExitGhostModeById(string playerId)
        {
            try
            {
                var p = Utils.GetPlayerById(playerId);
                if (p != null) { ExitGhostMode(p); return; }

                // No local Player => best-effort cleanup of tracking only.
                CleanupPlayerData(playerId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] ExitById({playerId}) error: {ex.Message}");
            }
        }

        //====================[ Query ]====================
        public static bool IsPlayerInGhostMode(string playerId)
        {
            bool anyBots   = _playerEnemyLists.TryGetValue(playerId, out var bl) && bl.Count > 0;
            bool anyGroups = _playerGroupLists.TryGetValue(playerId, out var gl) && gl.Count > 0;
            return anyBots || anyGroups;
        }

        //====================[ Helpers ]====================
        private static void ResetBuckets(string playerId)
        {
            _playerEnemyLists[playerId]   = new List<BotEnemiesController>();
            _playerGroupLists[playerId]   = new List<BotsGroup>();
            _originalEnemyInfos[playerId] = new Dictionary<BotEnemiesController, EnemyInfo>();
            _originalGroupSets[playerId]  = new HashSet<BotsGroup>();
        }

        private static void CleanupPlayerData(string playerId)
        {
            _playerEnemyLists.Remove(playerId);
            _playerGroupLists.Remove(playerId);
            _originalEnemyInfos.Remove(playerId);
            _originalGroupSets.Remove(playerId);
        }

        private static List<BotEnemiesController> FindAllBotControllers()
        {
            var list = new List<BotEnemiesController>();
            try
            {
                var owners = UnityEngine.Object.FindObjectsOfType<BotOwner>();
                foreach (var bo in owners)
                {
                    var ec = bo?.EnemiesController;
                    if (ec != null) list.Add(ec);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] FindAllBotControllers error: {ex.Message}");
            }
            return list;
        }

        private static List<BotsGroup> FindAllBotGroups()
        {
            var set = new HashSet<BotsGroup>();
            try
            {
                var owners = UnityEngine.Object.FindObjectsOfType<BotOwner>();
                foreach (var bo in owners)
                {
                    var g = bo?.BotsGroup;
                    if (g != null) set.Add(g);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] FindAllBotGroups error: {ex.Message}");
            }
            return new List<BotsGroup>(set);
        }
    }
}
