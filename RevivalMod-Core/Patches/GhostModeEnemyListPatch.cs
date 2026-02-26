//====================[ Imports ]====================
using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using RevivalMod.Helpers;
using SPT.Reflection.Patching;

namespace RevivalMod.Patches
{
    /// <summary>
    /// Harmony prefix on <see cref="BotsGroup.AddEnemy(IPlayer, EBotEnemyCause)"/>.
    /// Blocks bots from acquiring a player who is currently in ghost mode (downed/critical).
    ///
    /// Without this patch, bots immediately re-detect and re-add downed players after
    /// GhostMode.EnterGhostMode removes them, because the player is still alive (1 HP)
    /// and visible in the scene.
    /// </summary>
    internal class GhostModeAddEnemyPatch : ModulePatch
    {
        //====================[ Target Method ]====================
        protected override MethodBase GetTargetMethod()
        {
            // public bool BotsGroup.AddEnemy(IPlayer person, EBotEnemyCause cause)
            var method = AccessTools.Method(
                typeof(BotsGroup),
                nameof(BotsGroup.AddEnemy),
                new[] { typeof(IPlayer), typeof(EBotEnemyCause) });

            if (method == null)
                Plugin.LogSource.LogError("[GhostModeAddEnemyPatch] target method BotsGroup.AddEnemy not found!");

            return method;
        }

        //====================[ Prefix ]====================
        // ReSharper disable InconsistentNaming
        #pragma warning disable IDE1006, SA1313
        [PatchPrefix]
        private static bool PatchPrefix(IPlayer person, ref bool __result)
        #pragma warning restore IDE1006, SA1313
        // ReSharper restore InconsistentNaming
        {
            try
            {
                if (person != null && GhostMode.IsGhosted(person.ProfileId))
                {
                    // Block the add — player is downed and should be invisible to AI.
                    __result = false;
                    return false; // skip original
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GhostModeAddEnemyPatch] error: {ex.Message}");
            }

            return true; // run original
        }
    }
}
