using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace RevivalMod.Helpers
{
    /// <summary>
    /// Removes a player from AI enemy lists while downed/critical, then restores on exit.
    /// </summary>
    public static class GhostMode
    {
        private static readonly Dictionary<string, List<BotEnemiesController>> _playerEnemyLists = new();
        private static readonly Dictionary<string, List<BotsGroup>> _playerGroupLists = new();
        private static readonly Dictionary<string, Dictionary<BotEnemiesController, EnemyInfo>> _originalEnemyInfos = new();
        private static readonly Dictionary<string, HashSet<BotsGroup>> _originalGroupSets = new();

        public static void EnterGhostMode(Player player)
        {
            if (player == null) return;
            string playerId = player.ProfileId;

            try
            {
                Plugin.LogSource.LogInfo($"[GhostMode] Enter for {player.Profile.Nickname}");
                ResetBuckets(playerId);

                var allBots = FindAllBotControllers();
                var allGroups = FindAllBotGroups();

                foreach (var ec in allBots)
                {
                    if (ec == null || !ec.EnemyInfos.ContainsKey(player)) continue;
                    _originalEnemyInfos[playerId][ec] = ec.EnemyInfos[player];
                    ec.Remove(player);
                    _playerEnemyLists[playerId].Add(ec);
                }

                foreach (var g in allGroups)
                {
                    if (g == null || !g.Enemies.ContainsKey(player)) continue;
                    _originalGroupSets[playerId].Add(g);
                    g.RemoveEnemy(player, EBotEnemyCause.initial);
                    _playerGroupLists[playerId].Add(g);
                }

                Plugin.LogSource.LogInfo($"[GhostMode] Done: bots={_playerEnemyLists[playerId].Count}, groups={_playerGroupLists[playerId].Count}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] Enter error: {ex.Message}");
            }
        }

        public static void EnterGhostModeById(string playerId)
        {
            try
            {
                var p = Utils.GetPlayerById(playerId);
                if (p != null) { EnterGhostMode(p); return; }
                ResetBuckets(playerId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] EnterById({playerId}) error: {ex.Message}");
            }
        }

        public static void ExitGhostMode(Player player)
        {
            if (player == null) return;
            string playerId = player.ProfileId;

            try
            {
                Plugin.LogSource.LogInfo($"[GhostMode] Exit for {player.Profile.Nickname}");

                if (_originalEnemyInfos.TryGetValue(playerId, out var map))
                {
                    foreach (var kv in map)
                    {
                        if (kv.Key != null) kv.Key.SetInfo(player, kv.Value);
                    }
                }

                if (_originalGroupSets.TryGetValue(playerId, out var set))
                {
                    foreach (var g in set)
                    {
                        if (g != null) g.AddEnemy(player, EBotEnemyCause.initial);
                    }
                }

                CleanupPlayerData(playerId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] Exit error: {ex.Message}");
            }
        }

        public static void ExitGhostModeById(string playerId)
        {
            try
            {
                var p = Utils.GetPlayerById(playerId);
                if (p != null) { ExitGhostMode(p); return; }
                CleanupPlayerData(playerId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] ExitById({playerId}) error: {ex.Message}");
            }
        }

        public static bool IsPlayerInGhostMode(string playerId)
        {
            bool anyBots = _playerEnemyLists.TryGetValue(playerId, out var bl) && bl.Count > 0;
            bool anyGroups = _playerGroupLists.TryGetValue(playerId, out var gl) && gl.Count > 0;
            return anyBots || anyGroups;
        }

        private static void ResetBuckets(string playerId)
        {
            _playerEnemyLists[playerId] = new List<BotEnemiesController>();
            _playerGroupLists[playerId] = new List<BotsGroup>();
            _originalEnemyInfos[playerId] = new Dictionary<BotEnemiesController, EnemyInfo>();
            _originalGroupSets[playerId] = new HashSet<BotsGroup>();
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
                foreach (var bo in UnityEngine.Object.FindObjectsOfType<BotOwner>())
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
                foreach (var bo in UnityEngine.Object.FindObjectsOfType<BotOwner>())
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
