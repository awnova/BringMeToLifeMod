//====================[ Imports ]====================
using System;
using EFT;

namespace RevivalMod.Helpers
{
    //====================[ GodMode ]====================
    internal static class GodMode
    {
        //====================[ Query ]====================
        public static bool IsEnabled() => RevivalModSettings.GOD_MODE.Value;

        //====================[ Enable (config-gated) ]====================
        public static void Enable(Player player)
        {
            if (!IsEnabled() || player is null) return;
            ForceEnable(player);
        }

        //====================[ ForceEnable (always on) ]====================
        public static void ForceEnable(Player player)
        {
            if (player is null) return;
            try
            {
                var hc = player.ActiveHealthController;
                if (hc == null) return;
                hc.SetDamageCoeff(0f);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GodMode] ForceEnable error: {ex.Message}");
            }
        }

        //====================[ Disable ]====================
        public static void Disable(Player player)
        {
            if (player is null) return;
            try
            {
                var hc = player.ActiveHealthController;
                if (hc == null) return;
                hc.SetDamageCoeff(1f);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[GodMode] Disable error: {ex.Message}");
            }
        }
    }
}