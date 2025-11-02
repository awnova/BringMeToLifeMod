//====================[ Imports ]====================
using System;
using System.Reflection;
using EFT;
using EFT.Communications;
using EFT.HealthSystem;
using HarmonyLib;
using RevivalMod.Features;
using RevivalMod.Helpers;
using SPT.Reflection.Patching;

namespace RevivalMod.Patches
{
    //====================[ DeathPatch ]====================
    internal class DeathPatch : ModulePatch
    {
        //====================[ Target Method ]====================
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill));

        //====================[ Prefix Patch ]====================
        [PatchPrefix]
        private static bool Prefix(ActiveHealthController __instance, EDamageType damageType)
        {
            try
            {
                // Pull player from controller (allow AI to die normally)
                FieldInfo playerField = AccessTools.Field(typeof(ActiveHealthController), "Player");
                if (playerField?.GetValue(__instance) is not Player player || player.IsAI) return true;

                // Hardcore headshot rule bypasses death prevention
                if (DeathMode.ShouldAllowDeathFromHardcoreHeadshot(__instance, damageType)) return true;

                // Death-blocking decision
                bool shouldBlockDeath = DeathMode.ShouldBlockDeath(player, damageType);
                if (shouldBlockDeath)
                {
                    // Enter critical state instead of dying
                    RevivalFeatures.SetPlayerCriticalState(player, true, damageType);
                    return false; // block Kill()
                }

                return true; // allow Kill()
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in Death prevention patch: {ex.Message}");
                return true; // fail-open on error
            }
        }
    }
}