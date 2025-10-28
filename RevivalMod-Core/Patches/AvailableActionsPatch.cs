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
            // Add the interactions to the list. 
            if (interactive is not BodyInteractable revive) 
                return true;

            // Null check to prevent errors
            if (revive.Revivee == null)
            {
                Plugin.LogSource.LogError("AvailableActionsPatch: BodyInteractable.Revivee is null");
                return true;
            }

            if (owner?.Player == null)
            {
                Plugin.LogSource.LogError("AvailableActionsPatch: Owner or Owner.Player is null");
                return true;
            }
            
            Plugin.LogSource.LogDebug($"BodyInteractable.Revivee is player {revive.Revivee.ProfileId} and interactor is {owner.Player.ProfileId}");

            ActionsReturnClass newResult = revive.GetActions(owner);
            __result = newResult;
            return false;

        }
    }
}