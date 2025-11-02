//====================[ Imports ]====================
using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using RevivalMod.Features;
using RevivalMod.Helpers;
using SPT.Reflection.Patching;

namespace RevivalMod.Patches
{
    //====================[ GhostModeCriticalStatePatch ]====================
    internal class GhostModeCriticalStatePatch : ModulePatch
    {
        //====================[ Target Method ]====================
        protected override MethodBase GetTargetMethod() =>
            // Hook into RevivalFeatures.SetPlayerCriticalState
            AccessTools.Method(typeof(RevivalFeatures), "SetPlayerCriticalState");

        //====================[ Postfix ]====================
        [PatchPostfix]
        private static void PatchPostfix(
            Player player,
            [HarmonyArgument(1)] bool isCritical,
            EDamageType damageType)
        {
            try
            {
                if (player == null || !player.IsYourPlayer) return;

                // Toggle ghost mode to match critical state; avoid redundant transitions
                bool inGhost = GhostMode.IsPlayerInGhostMode(player.ProfileId);
                if (isCritical && !inGhost) GhostMode.EnterGhostMode(player);
                else if (!isCritical && inGhost) GhostMode.ExitGhostMode(player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode (critical state): {ex.Message}");
            }
        }
    }

    //====================[ GhostModeRevivalPatch ]====================
    internal class GhostModeRevivalPatch : ModulePatch
    {
        //====================[ Target Method ]====================
        protected override MethodBase GetTargetMethod()
        {
            var method = AccessTools.Method(typeof(RevivalFeatures), "TryPerformRevivalByTeammate");
            if (method == null) Plugin.LogSource.LogError("GhostModeRevivalPatch: target not found");
            return method;
        }

        //====================[ Postfix ]====================
        // ReSharper disable InconsistentNaming
        #pragma warning disable IDE1006, SA1313 // Harmony requires __result magic name
        [PatchPostfix]
        private static void PatchPostfix([HarmonyArgument(0)] string reviveeId, bool __result)
        #pragma warning restore IDE1006, SA1313
        // ReSharper restore InconsistentNaming
        {
            try
            {
                // Prefer positive branch to avoid "redundant control flow jump" warnings
                if (__result)
                {
                    var player = Utils.GetPlayerById(reviveeId);
                    if (player != null && player.IsYourPlayer)
                    {
                        // Exit ghost mode when the local player is revived
                        GhostMode.ExitGhostMode(player);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode (revival): {ex.Message}");
            }
        }
    }

    //====================[ GhostModeDeathPatch ]====================
    internal class GhostModeDeathPatch : ModulePatch
    {
        //====================[ Target Method ]====================
        protected override MethodBase GetTargetMethod()
        {
            var method = AccessTools.Method(typeof(RevivalFeatures), "ForcePlayerDeath");
            if (method == null) Plugin.LogSource.LogError("GhostModeDeathPatch: target not found");
            return method;
        }

        //====================[ Postfix ]====================
        [PatchPostfix]
        private static void PatchPostfix([HarmonyArgument(0)] object targetArg)
        {
            try
            {
                Player player = targetArg as Player;
                if (player == null && targetArg is string id) player = Utils.GetPlayerById(id);
                if (player == null || !player.IsYourPlayer) return;

                // Cleanup handled elsewhere; intentionally no ghost-mode call here.
                // GhostMode.ExitGhostMode(player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"GhostMode (death): {ex.Message}");
            }
        }
    }
}
