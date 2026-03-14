using Fika.Core.Main.Utils;

namespace KeepMeAlive.Helpers
{
    /// <summary>
    /// Step-by-step debug logging for the revive flow. Always on, no config.
    /// Log format: [ReviveDebug:STEP] machine=Headless|Client server={T|F} playerId=... isLocal={T|F} step=... details
    /// </summary>
    internal static class ReviveDebug
    {
        public static void Log(string step, string playerId, bool isLocal, string details = null)
        {
            string machine = FikaBackendUtils.IsHeadless ? "Headless" : "Client";
            string server = FikaBackendUtils.IsServer ? "T" : "F";
            string local = isLocal ? "T" : "F";
            string tail = string.IsNullOrEmpty(details) ? string.Empty : $" {details}";
            Plugin.LogSource.LogInfo($"[ReviveDebug:{step}] machine={machine} server={server} playerId={playerId} isLocal={local}{tail}");
        }
    }
}
