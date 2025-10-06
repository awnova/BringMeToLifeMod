using System.Reflection;
using EFT;
using HarmonyLib;
using RevivalMod.Components;
using SPT.Reflection.Patching;

namespace RevivalMod.Patches
{
    public class AvailableActionsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.FirstMethod(typeof(GetActionsClass), method => method.Name == nameof(GetActionsClass.GetAvailableActions) && method.GetParameters()[0].Name == "owner");
        }

        [PatchPrefix]
        static bool PatchPrefix(GamePlayerOwner owner, GInterface150 interactive, ref ActionsReturnClass __result)
        {

            if (interactive == null) return true; // Proceed with original method if no interactive object

            // Add the interactions to the list. 
            if (interactive is BodyInteractable)
            {
                BodyInteractable revive = interactive as BodyInteractable;

                Plugin.LogSource.LogDebug($"BodyInteractable.Revivee is player {revive.Revivee.PlayerId} and interactor is {owner.Player.PlayerId}");
                
                ActionsReturnClass newResult = revive.GetActions(owner);
                __result = newResult;
                return false;                             
            }
            return true;
        }
    }
}