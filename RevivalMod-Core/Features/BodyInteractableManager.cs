using System;
using System.Collections.Generic;
using EFT;
using EFT.HealthSystem;
using KeepMeAlive.Components;
using UnityEngine;

namespace KeepMeAlive.Features
{
    /// <summary>
    /// Manages BodyInteractable collider state so downed/injured players can be interacted with.
    /// </summary>
    internal static class BodyInteractableManager
    {
        private static readonly Dictionary<string, BodyInteractable> _cache = new Dictionary<string, BodyInteractable>();

        private static readonly EBodyPart[] TrackedBodyParts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach,
            EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg
        };

        public static void Tick(Player player)
        {
            if (player?.HealthController == null || player.IsAI) return;

            try
            {
                bool isCritical = RMSession.IsPlayerCritical(player.ProfileId);

                var pst = RMSession.GetPlayerState(player.ProfileId);
                bool isRevived = pst?.State == RMState.Revived;

                bool isInjured = false;

                if (!isCritical && !isRevived)
                {
                    for (int i = 0; i < TrackedBodyParts.Length; i++)
                    {
                        var hp = player.HealthController.GetBodyPartHealth(TrackedBodyParts[i]);
                        if (hp.Current >= hp.Maximum) continue;
                        isInjured = true;
                        break;
                    }
                }

                bool shouldEnable = isCritical || isRevived || isInjured;

                if (!_cache.TryGetValue(player.ProfileId, out var bi) || bi == null)
                {
                    foreach (var found in player.GetComponentsInChildren<BodyInteractable>(true))
                    {
                        if (found.Revivee?.ProfileId == player.ProfileId)
                        {
                            _cache[player.ProfileId] = bi = found;
                            break;
                        }
                    }
                }

                if (bi != null)
                {
                    bool canEnable = shouldEnable && !bi.HasActivePicker;
                    var cols = bi.GetComponents<Collider>();
                    for (int i = 0; i < cols.Length; i++)
                    {
                        var col = cols[i];
                        if (col != null && col.enabled != canEnable)
                        {
                            col.enabled = canEnable;
                        }
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[BodyInteractableManager] Tick error: {ex.Message}"); }
        }

        public static void ForceClosePicker(string playerId)
        {
            if (_cache.TryGetValue(playerId, out var cachedBi) && cachedBi != null)
                cachedBi.ForceClosePicker();
        }

        public static void Remove(string playerId)
        {
            _cache.Remove(playerId);
        }
    }
}
