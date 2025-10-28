//====================[ Imports ]====================
using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using RevivalMod.Components;
using RevivalMod.Helpers;
using SPT.Reflection.Patching;
using UnityEngine;

//====================[ OnPlayerCreatedPatch ]====================
namespace RevivalMod.Patches
{
    internal class OnPlayerCreatedPatch : ModulePatch
    {
        //====================[ Hook Target ]====================
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Property(typeof(Player), nameof(Player.PlayerId)).GetSetMethod();

        //====================[ When Player Spawns ]====================
        [PatchPostfix]
        private static void Postfix(ref Player __instance)
        {
            if (__instance == null) return;
            if (__instance.gameObject == null) return;
            if (__instance.gameObject.name.Contains("Bot")) return; // players only
            
            AttachBodyInteractable(__instance);
        }

        //====================[ Attach Interactable ]====================
        private static void AttachBodyInteractable(Player player)
        {
            try
            {
                if (player == null)
                {
                    Plugin.LogSource.LogError("AttachBodyInteractable: Player is null");
                    return;
                }

                if (player.gameObject == null)
                {
                    Plugin.LogSource.LogError("AttachBodyInteractable: Player.gameObject is null");
                    return;
                }

                if (player.gameObject.transform == null)
                {
                    Plugin.LogSource.LogError("AttachBodyInteractable: Player.gameObject.transform is null");
                    return;
                }

                // Use gameObject.transform (Unity Transform) instead of player.Transform (EFT wrapper)
                var anchor = FindBackAnchor(player.gameObject.transform) ?? player.gameObject.transform;
                Plugin.LogSource.LogInfo($"Adding BodyInteractable to {player.PlayerId}");

                var obj = InteractableBuilder<BodyInteractable>.Build(
                    "Body Interactable",
                    Vector3.zero,
                    Vector3.one * RevivalModSettings.REVIVAL_RANGE.Value,
                    anchor,
                    player,
                    RevivalModSettings.TESTING.Value
                );

                if (obj == null)
                {
                    Plugin.LogSource.LogError($"AttachBodyInteractable: InteractableBuilder returned null for {player.PlayerId}");
                    return;
                }

                var bodyInteractable = obj.GetComponent<BodyInteractable>();
                if (bodyInteractable == null)
                {
                    Plugin.LogSource.LogError($"AttachBodyInteractable: BodyInteractable component is null for {player.PlayerId}");
                    return;
                }

                if (bodyInteractable.Revivee == null)
                {
                    Plugin.LogSource.LogError($"AttachBodyInteractable: BodyInteractable.Revivee is null for {player.PlayerId}");
                    return;
                }

                Plugin.LogSource.LogInfo(
                    $"BodyInteractable.Revivee={bodyInteractable.Revivee.PlayerId} for {player.PlayerId}"
                );
            }
            catch (Exception ex)
            {
                string playerId = "unknown";
                try
                {
                    if (player != null && !string.IsNullOrEmpty(player.ProfileId))
                        playerId = player.ProfileId;
                }
                catch { /* ignore */ }
                
                Plugin.LogSource.LogError($"AttachBodyInteractable error for player {playerId}: {ex.Message}");
                Plugin.LogSource.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        //====================[ Find Attach Point ]====================
        private static Transform FindBackAnchor(Transform root)
        {
            try
            {
                return root.GetChild(0).GetChild(4).GetChild(0).GetChild(2).GetChild(4).GetChild(0).GetChild(11);
            }
            catch { /* fall through */ }

            foreach (var name in new[] { "Spine3", "Spine2", "Spine1", "Back", "Spine", "Root" })
            {
                var t = root.Find(name);
                if (t != null) return t;
            }
            return null;
        }
    }
}
