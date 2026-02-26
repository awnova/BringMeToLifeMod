using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace RevivalMod.Helpers
{
    /// <summary>
    /// Removes a player from AI enemy lists while downed/critical, then re-adds on exit.
    ///
    /// Key design:
    ///   • Enter: removes the player from every current BotEnemiesController + BotsGroup
    ///     AND registers the player as "ghosted" so the AddEnemy Harmony patch blocks
    ///     bots from re-acquiring the player while downed.
    ///   • Exit: un-registers the ghosted flag, then does a FRESH scan of all bot groups
    ///     and forcefully re-adds the player as an enemy.  This handles bots that spawned
    ///     while the player was downed (which the old snapshot approach missed).
    /// </summary>
    public static class GhostMode
    {
        // ── Persistent ghosted set (checked by GhostModeAddEnemyPatch) ──
        private static readonly HashSet<string> _ghostedPlayers = new();

        /// <summary>
        /// Returns true if the given player profile is currently ghosted
        /// (should be invisible to AI).  Called by the AddEnemy patch.
        /// </summary>
        public static bool IsGhosted(string profileId) => _ghostedPlayers.Contains(profileId);

        public static bool IsPlayerInGhostMode(string profileId) => _ghostedPlayers.Contains(profileId);

        // ── Enter ───────────────────────────────────────────────────────

        public static void EnterGhostMode(Player player)
        {
            if (player == null) return;
            string playerId = player.ProfileId;

            try
            {
                Plugin.LogSource.LogInfo($"[GhostMode] Enter for {player.Profile.Nickname} ({playerId})");

                // Mark as ghosted FIRST so the AddEnemy patch blocks re-acquisition
                // even if a bot tries to add the player mid-removal.
                _ghostedPlayers.Add(playerId);

                int removedBots = 0;
                int removedGroups = 0;

                // Remove from every BotEnemiesController
                foreach (var bo in FindAllBotOwners())
                {
                    var ec = bo?.EnemiesController;
                    if (ec == null || !ec.EnemyInfos.ContainsKey(player)) continue;

                    try { ec.Remove(player); removedBots++; }
                    catch (Exception ex)
                    {
                        Plugin.LogSource.LogWarning($"[GhostMode] ec.Remove error: {ex.Message}");
                    }
                }

                // Remove from every BotsGroup
                foreach (var g in FindAllBotGroups())
                {
                    if (g == null || !g.Enemies.ContainsKey(player)) continue;

                    try { g.RemoveEnemy(player, EBotEnemyCause.initial); removedGroups++; }
                    catch (Exception ex)
                    {
                        Plugin.LogSource.LogWarning($"[GhostMode] g.RemoveEnemy error: {ex.Message}");
                    }
                }

                Plugin.LogSource.LogInfo($"[GhostMode] Enter done: removed from {removedBots} bot controllers, {removedGroups} groups");
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
                if (p != null)
                {
                    EnterGhostMode(p);
                }
                else
                {
                    // Player object not available on this machine — just set the flag
                    // so the AddEnemy patch blocks any future acquisition.
                    _ghostedPlayers.Add(playerId);
                    Plugin.LogSource.LogWarning($"[GhostMode] EnterById: player object not found for {playerId}, flag set only");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] EnterById({playerId}) error: {ex.Message}");
            }
        }

        // ── Exit ────────────────────────────────────────────────────────

        public static void ExitGhostMode(Player player)
        {
            if (player == null) return;
            string playerId = player.ProfileId;

            if (!_ghostedPlayers.Remove(playerId))
            {
                // Not ghosted — nothing to do.
                return;
            }

            try
            {
                Plugin.LogSource.LogInfo($"[GhostMode] Exit for {player.Profile.Nickname} ({playerId})");

                int addedGroups = 0;

                // Do a FRESH scan: add the revived player as an enemy to every bot
                // group that doesn't already have them.  BotsGroup.AddEnemy cascades
                // into BotMemoryClass.AddEnemy for every group member, so we don't
                // need to touch individual BotEnemiesControllers.
                foreach (var g in FindAllBotGroups())
                {
                    if (g == null || g.Enemies.ContainsKey(player)) continue;

                    // Skip if the player is a member of this group (friendly AI squad)
                    bool isMember = false;
                    for (int i = 0; i < g.MembersCount; i++)
                    {
                        if (g.Member(i)?.GetPlayer?.Id == player.Id)
                        {
                            isMember = true;
                            break;
                        }
                    }
                    if (isMember) continue;

                    try
                    {
                        if (g.AddEnemy(player, EBotEnemyCause.initial))
                            addedGroups++;
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogSource.LogWarning($"[GhostMode] g.AddEnemy error: {ex.Message}");
                    }
                }

                Plugin.LogSource.LogInfo($"[GhostMode] Exit done: re-added to {addedGroups} bot groups");
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
                if (p != null)
                {
                    ExitGhostMode(p);
                }
                else
                {
                    // Player object not available — just clear the flag.
                    _ghostedPlayers.Remove(playerId);
                    Plugin.LogSource.LogWarning($"[GhostMode] ExitById: player object not found for {playerId}, flag cleared only");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] ExitById({playerId}) error: {ex.Message}");
            }
        }

        // ── Cleanup ─────────────────────────────────────────────────────

        /// <summary>
        /// Clears the ghosted flag WITHOUT re-adding to enemy lists.
        /// Use when the player is truly dying (ForceBleedout) — BSG's own
        /// death handlers will clean up enemy lists after Kill() runs.
        /// </summary>
        public static void ClearGhostFlag(string playerId)
        {
            _ghostedPlayers.Remove(playerId);
        }

        /// <summary>
        /// Clears all ghost state (e.g. on raid end).
        /// </summary>
        public static void Reset()
        {
            _ghostedPlayers.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static List<BotOwner> FindAllBotOwners()
        {
            var list = new List<BotOwner>();
            try
            {
                foreach (var bo in UnityEngine.Object.FindObjectsOfType<BotOwner>())
                {
                    if (bo != null) list.Add(bo);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostMode] FindAllBotOwners error: {ex.Message}");
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
