//====================[ Imports ]====================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.InputSystem;
using KeepMeAlive.Components;
using KeepMeAlive.Helpers;
using UnityEngine;

namespace KeepMeAlive.Features
{
    //====================[ DownedMovementController ]====================
    internal static class DownedMovementController
    {
        //====================[ Ignored-Command Infrastructure ]====================
        private static HashSet<ECommand> _ignoredCommands;
        private static bool _reflectionFailed;

        //====================[ Stance Commands ]====================
        private static readonly ECommand[] StanceCommands =
        {
            ECommand.ToggleDuck,
            ECommand.ToggleProne,
            ECommand.NextWalkPose,
            ECommand.PreviousWalkPose,
            ECommand.Jump,
            ECommand.RestorePose
        };

        //====================[ Weapon-Select Commands ]====================
        private static readonly ECommand[] WeaponSelectCommands =
        {
            ECommand.SelectFirstPrimaryWeapon,
            ECommand.SelectSecondPrimaryWeapon,
            ECommand.SelectSecondaryWeapon,
            ECommand.SelectKnife,
            ECommand.QuickSelectSecondaryWeapon,
            ECommand.SelectFastSlot4,
            ECommand.SelectFastSlot5,
            ECommand.SelectFastSlot6,
            ECommand.SelectFastSlot7,
            ECommand.SelectFastSlot8,
            ECommand.SelectFastSlot9,
            ECommand.SelectFastSlot0,
            ECommand.QuickKnifeKick
        };

        //====================[ Reflection Accessor ]====================
        private static HashSet<ECommand> GetIgnoredCommands()
        {
            if (_ignoredCommands != null) return _ignoredCommands;
            if (_reflectionFailed) return null;

            try
            {
                var field = typeof(PlayerOwner).GetField("ignoredCommands",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                if (field == null)
                {
                    Plugin.LogSource.LogError("[DownedMovement] ignoredCommands field not found via reflection");
                    _reflectionFailed = true;
                    return null;
                }
                _ignoredCommands = field.GetValue(null) as HashSet<ECommand>;
                if (_ignoredCommands == null)
                {
                    Plugin.LogSource.LogError("[DownedMovement] ignoredCommands field was null");
                    _reflectionFailed = true;
                }
                return _ignoredCommands;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[DownedMovement] ignoredCommands reflection error: {ex.Message}");
                _reflectionFailed = true;
                return null;
            }
        }

        private static void AddIgnored(ECommand[] commands)
        {
            var set = GetIgnoredCommands();
            if (set == null) return;
            for (int i = 0; i < commands.Length; i++) set.Add(commands[i]);
        }

        private static void RemoveIgnored(ECommand[] commands)
        {
            var set = GetIgnoredCommands();
            if (set == null) return;
            for (int i = 0; i < commands.Length; i++) set.Remove(commands[i]);
        }

        //====================[ Public API ]====================

        /// <summary>
        /// One-shot: set player prone, then block all stance-changing inputs so the player cannot stand up.
        /// </summary>
        public static void ForceProne(Player player)
        {
            try
            {
                if (player?.MovementContext == null) return;

                var mc = player.MovementContext;
                mc.EnableSprint(false);
                mc.SetPoseLevel(0f, true);
                mc.IsInPronePose = true;

                AddIgnored(StanceCommands);

                Plugin.LogSource.LogInfo($"[DownedMovement] ForceProne applied for {player.ProfileId}");
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedMovement] ForceProne error: {ex.Message}"); }
        }

        /// <summary>
        /// Undo ForceProne: remove the stance-blocking commands from the ignore list.
        /// Does NOT change pose — caller is responsible for restoring pose if needed.
        /// </summary>
        public static void ReleaseProne(Player player)
        {
            try
            {
                RemoveIgnored(StanceCommands);
                Plugin.LogSource.LogInfo($"[DownedMovement] ReleaseProne applied for {player?.ProfileId}");
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedMovement] ReleaseProne error: {ex.Message}"); }
        }

        /// <summary>
        /// One-shot: set hands empty, then block all weapon-select inputs so the player cannot draw weapons.
        /// </summary>
        public static void ForceEmptyHands(Player player)
        {
            try
            {
                if (player == null) return;

                player.SetEmptyHands(null);
                AddIgnored(WeaponSelectCommands);

                Plugin.LogSource.LogInfo($"[DownedMovement] ForceEmptyHands applied for {player.ProfileId}");
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedMovement] ForceEmptyHands error: {ex.Message}"); }
        }

        /// <summary>
        /// Undo ForceEmptyHands: remove weapon-select commands from the ignore list.
        /// Does NOT equip a weapon — caller is responsible for that.
        /// </summary>
        public static void ReleaseEmptyHands(Player player)
        {
            try
            {
                RemoveIgnored(WeaponSelectCommands);
                Plugin.LogSource.LogInfo($"[DownedMovement] ReleaseEmptyHands applied for {player?.ProfileId}");
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedMovement] ReleaseEmptyHands error: {ex.Message}"); }
        }

        //====================[ Legacy Public API ]====================
        // Full downed-entry orchestration: prone, empty hands, unhook events, vocalize, release turrets.
        public static void ApplyRevivableState(Player player)
        {
            try
            {
                ForceProne(player);
                ForceEmptyHands(player);

                if (player.ShouldVocalizeDeath(player.LastDamagedBodyPart))
                {
                    var trig = player.LastDamageType.IsWeaponInduced() ? EPhraseTrigger.OnDeath : EPhraseTrigger.OnAgony;
                    try { player.Speaker.Play(trig, player.HealthStatus, true, null); } catch { }
                }

                var mc = player.MovementContext;
                mc.ReleaseDoorIfInteractingWithOne();
                mc.OnStateChanged -= player.method_17;
                mc.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;

                if (mc.StationaryWeapon != null)
                {
                    mc.StationaryWeapon.Unlock(player.ProfileId);
                    if (mc.StationaryWeapon.Item == player.HandsController.Item)
                    {
                        mc.StationaryWeapon.Show();
                        player.ReleaseHand();
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedMovement] ApplyRevivableState error: {ex.Message}"); }
        }

        // Re-subscribe movement and animation event hooks that were stripped when the player went down.
        public static void ReattachMovementHooks(Player player)
        {
            if (player.MovementContext == null) return;
            try
            {
                var mc = player.MovementContext;
                mc.OnStateChanged -= player.method_17;
                mc.OnStateChanged += player.method_17;
                mc.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
                mc.PhysicalConditionChanged += player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[DownedMovement] Re-hook movement events error: {ex.Message}"); }
        }

        // Scale walk speed from downed settings, or freeze movement while revive flow is active.
        public static void ApplyDownedMovementSpeed(Player player, RMPlayer st)
        {
            try
            {
                bool frozen = st.State == RMState.Reviving || st.IsBeingRevived || st.IsSelfReviving || st.SelfReviveAuthPending || st.SelfReviveHoldTime > 0f;
                float baseSpd = st.OriginalMovementSpeed > 0 ? st.OriginalMovementSpeed : player.Physical.WalkSpeedLimit;
                player.Physical.WalkSpeedLimit = frozen ? 0f : Mathf.Max(0.1f, baseSpd * (KeepMeAliveSettings.DOWNED_MOVEMENT_SPEED.Value / 100f));
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedMovement] ApplyDownedMovementSpeed error: {ex.Message}"); }
        }
    }
}
