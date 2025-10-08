using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using RevivalMod.Features;

namespace RevivalMod.Patches
{
    internal class ApplyDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage));;
        }

        [PatchPrefix]
        private static bool PatchPrefix(ActiveHealthController __instance)
        {
            Player player = __instance.Player;

            if (player == null | !player.IsYourPlayer)
                return true;

            if (RevivalFeatures.IsPlayerInCriticalState(player.ProfileId))
                return false;

            return true;
        }
    }
}