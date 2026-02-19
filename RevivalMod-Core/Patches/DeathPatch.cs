using System;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using RevivalMod.Features;
using RevivalMod.Helpers;
using RevivalMod.Components;
using SPT.Reflection.Patching;

namespace RevivalMod.Patches
{
    internal class DeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill));

        [PatchPrefix]
        private static bool Prefix(ActiveHealthController __instance, EDamageType damageType)
        {
            try
            {
                FieldInfo playerField = AccessTools.Field(typeof(ActiveHealthController), "Player");
                if (playerField?.GetValue(__instance) is not Player player || player.IsAI) return true;

                string playerId = player.ProfileId;
                bool isLocalPlayer = player.IsYourPlayer;

                if (DeathMode.ShouldAllowDeathFromHardcoreHeadshot(__instance, damageType)) return true;

                if (RMSession.HasPlayerState(playerId))
                {
                    var st = RMSession.GetPlayerState(playerId);

                    if (st.State is RMState.BleedingOut or RMState.Reviving or RMState.Revived)
                    {
                        if (!isLocalPlayer && st.State == RMState.BleedingOut)
                            return false;

                        if (DeathMode.ShouldBlockDeath(player, damageType))
                            return false;
                    }
                }

                if (DeathMode.ShouldBlockDeath(player, damageType))
                {
                    RevivalFeatures.SetPlayerCriticalState(player, true, damageType);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in Death prevention patch: {ex.Message}");
                return true;
            }
        }
    }
}
