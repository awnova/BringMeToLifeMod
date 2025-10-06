using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using RevivalMod.Components;
using RevivalMod.Helpers;

namespace RevivalMod.Patches
{
    public class OnPlayerCreatedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Property(typeof(Player), nameof(Player.PlayerId)).GetSetMethod();
        }

        [PatchPostfix]
        static void Postfix(ref Player __instance)
        {
            // Try to only apply to real players, not bots.
            if (!__instance.gameObject.name.Contains("Bot"))
            {
                InteractableInit(ref __instance);                
            }
        }

        private static void InteractableInit(ref Player __instance)
        {
            Plugin.LogSource.LogInfo($"Adding body interactable to player {__instance.PlayerId}");

            Transform backTransform = __instance.gameObject.transform.GetChild(0).GetChild(4).GetChild(0)
                .GetChild(2).GetChild(4).GetChild(0).GetChild(11);

            // Add Interactables
            GameObject obj = InteractableBuilder<BodyInteractable>.Build(
                "Body Interactable",
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(0.3f, 0.3f, 0.3f),
                backTransform,
                __instance,
                RevivalModSettings.TESTING.Value);

            Plugin.LogSource.LogInfo($"BodyInteractable.Revivee set to player {obj.GetComponent<BodyInteractable>().Revivee.PlayerId} for player {__instance.PlayerId}");
         
        }
    }
}