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
        private static bool PatchPrefix(GamePlayerOwner owner, GInterface150 interactive, ref ActionsReturnClass __result)
        {

            switch (interactive)
            {
                case null:
                    return true; // Proceed with original method if no interactive object
                
                // Add the interactions to the list. 
                case BodyInteractable interactable:
                {
                    Plugin.LogSource.LogDebug($"BodyInteractable.Revivee is player {interactable.Revivee.ProfileId} and interactor is {owner.Player.ProfileId}");

                    ActionsReturnClass newResult = interactable.GetActions(owner);
                    
                    __result = newResult;
                    
                    return false;
                }
                default:
                    return true;
            }
        }
    }
}