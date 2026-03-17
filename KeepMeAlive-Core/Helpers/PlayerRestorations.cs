//====================[ Imports ]====================
using System;
using Comfort.Common;
using EFT;
using KeepMeAlive.Components;

namespace KeepMeAlive.Helpers
{
    //====================[ PlayerRestorations ]====================
    internal static class PlayerRestorations
    {
        //====================[ Movement Restoration ]====================
        public static void StoreOriginalMovementSpeed(Player player)
        {
            if (player is null)
            {
                return;
            }
            
            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);
                if (st.OriginalMovementSpeed < 0)
                {
                    st.OriginalMovementSpeed = player.Physical.WalkSpeedLimit;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PlayerRestorations] StoreOriginalMovementSpeed: {ex.Message}");
            }
        }

        public static void RestorePlayerMovement(Player player, bool forceStandingPose = true)
        {
            if (player is null)
            {
                return;
            }
            
            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);
                if (st.OriginalMovementSpeed > 0)
                {
                    player.Physical.WalkSpeedLimit = st.OriginalMovementSpeed;
                }

                if (forceStandingPose)
                {
                    player.MovementContext.SetPoseLevel(1f);
                }
                player.MovementContext.EnableSprint(true);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PlayerRestorations] RestorePlayerMovement: {ex.Message}");
            }
        }

        public static void RestorePlayerWeapon(Player player)
        {
            if (player is null || !player.IsYourPlayer) return;
            
            try
            {
                // Force hands to equip something if they are empty
                if (player.HandsController is EFT.Player.EmptyHandsController)
                {
                    player.SetFirstAvailableItem((Result<IHandsController> _) => { });
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PlayerRestorations] RestorePlayerWeapon error: {ex}");
            }
        }

        //====================[ Awareness Restoration ]====================
        public static void SetAwarenessZero(Player player)
        {
            if (player is null)
            {
                return;
            }
            
            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);
                if (!st.HasStoredAwareness)
                {
                    st.OriginalAwareness = player.Awareness;
                    st.HasStoredAwareness = true;
                }
                
                player.Awareness = 0f;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PlayerRestorations] SetAwarenessZero: {ex.Message}");
            }
        }

        public static void RestoreAwareness(Player player)
        {
            if (player is null)
            {
                return;
            }
            
            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);
                if (st.HasStoredAwareness)
                {
                    player.Awareness = st.OriginalAwareness;
                    st.HasStoredAwareness = false;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[PlayerRestorations] RestoreAwareness: {ex.Message}");
            }
        }
    }
}