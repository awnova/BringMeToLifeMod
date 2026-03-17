//====================[ Imports ]====================
using System;
using System.Collections.Generic;
using EFT;
using KeepMeAlive.Components;

namespace KeepMeAlive.Features
{
    //====================[ BodyInteractableRuntime ]====================
    // Central runtime orchestration for body interactable lifecycle and cache.
    internal static class BodyInteractableRuntime
    {
        //====================[ Cache & Tracked Parts ]====================
        private static readonly Dictionary<string, BodyInteractable> Cache = new Dictionary<string, BodyInteractable>();

        public static readonly EBodyPart[] TrackedBodyParts =
        {
            EBodyPart.Head,
            EBodyPart.Chest,
            EBodyPart.Stomach,
            EBodyPart.LeftArm,
            EBodyPart.RightArm,
            EBodyPart.LeftLeg,
            EBodyPart.RightLeg
        };

        //====================[ Public API ]====================
        public static bool TryRouteActions(GamePlayerOwner owner, GInterface150 interactive, ref ActionsReturnClass result)
        {
            if (interactive is BodyInteractable body)
            {
                result = body.GetActions(owner);
                return true;
            }

            if (interactive is MedPickerInteractable picker)
            {
                result = picker.GetActions(owner);
                return true;
            }

            return false;
        }

        //====================[ Lifecycle ]====================
        public static void AttachToPlayer(Player player)
        {
            try
            {
                if (player == null || player.gameObject == null)
                {
                    Plugin.LogSource.LogError("AttachToPlayer: Player or transform is null");
                    return;
                }

                if (player.IsAI || player.AIData?.IsAI == true)
                {
                    return;
                }

                // The new AttachToPlayer is handled dynamically and directly on the Neck.
                BodyInteractable.AttachToPlayer(player);
                Plugin.LogSource.LogInfo($"Initiated BodyInteractable attachment routine for PlayerId {player.Id}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"AttachToPlayer error for player {(player != null ? player.Id.ToString() : "null")}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public static void Register(string profileId, BodyInteractable interactable)
        {
            if (string.IsNullOrEmpty(profileId) || interactable == null) return;
            Cache[profileId] = interactable;
        }

        public static void Tick(Player player)
        {
            // Tick no longer actively polls GetComponentsInChildren. 
            // BodyInteractable registers itself upon instantiation.
        }

        //====================[ Cleanup ]====================
        public static void Remove(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            Cache.Remove(playerId);
        }

        public static void ForceClosePicker(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return;

            try
            {
                if (Cache.TryGetValue(playerId, out var interactable) && interactable != null)
                {
                    interactable.ForceClosePicker();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[BodyInteractableRuntime] ForceClosePicker error: {ex.Message}");
            }
        }
    }
}