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
            if (__instance.gameObject.name.Contains("Bot")) return; // players only
            AttachBodyInteractable(__instance);
        }

        //====================[ Attach Interactable ]====================
        private static void AttachBodyInteractable(Player player)
        {
            try
            {
                var anchor = FindBackAnchor(player.Transform.Original) ?? player.Transform.Original;
                Plugin.LogSource.LogInfo($"Adding BodyInteractable to {player.PlayerId}");

                var obj = InteractableBuilder<BodyInteractable>.Build(
                    "Body Interactable",
                    Vector3.zero,
                    Vector3.one * RevivalModSettings.REVIVAL_RANGE.Value,
                    anchor,
                    player,
                    RevivalModSettings.TESTING.Value
                );

                Plugin.LogSource.LogInfo(
                    $"BodyInteractable.Revivee={obj.GetComponent<BodyInteractable>().Revivee.PlayerId} for {player.PlayerId}"
                );
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"AttachBodyInteractable error: {ex.Message}");
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
