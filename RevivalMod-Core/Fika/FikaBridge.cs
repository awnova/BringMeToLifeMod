//====================[ Imports ]====================
using Comfort.Common;
using Fika.Core.Main.Utils;
using Fika.Core.Networking;
using SPT.Reflection.Utils;

namespace RevivalMod.Fika
{
    //====================[ FikaBridge ]====================
    internal class FikaBridge
    {
        //====================[ Lifecycle ]====================
        public static void PluginEnable()
        {
            if (!Plugin.FikaInstalled) return;

            FikaMethods.InitOnPluginEnabled();
            Plugin.LogSource.LogInfo("Fika integration initialized!");
        }

        //====================[ Host / Identity ]====================
        public static bool IAmHost()
        {
            if (!Plugin.FikaInstalled) return true;
            return Singleton<FikaServer>.Instantiated;
        }

        public static string GetRaidId()
        {
            if (!Plugin.FikaInstalled)
                return ClientAppUtils.GetMainApp().GetClientBackEndSession().Profile.ProfileId;

            return FikaBackendUtils.GroupId;
        }

        //====================[ Revival Packet Wrappers ]====================
        public static void SendBleedingOutPacket(string playerId, float timeRemaining)
        {
            if (!Plugin.FikaInstalled) return;

            Plugin.LogSource.LogDebug($"Sending bleeding out packet for {playerId}");
            FikaMethods.SendBleedingOutPacket(playerId, timeRemaining);
        }

        public static void SendTeamHelpPacket(string reviveeId, string reviverId)
        {
            if (!Plugin.FikaInstalled) return;

            Plugin.LogSource.LogDebug($"Sending team help packet: {reviverId} helping {reviveeId}");
            FikaMethods.SendTeamHelpPacket(reviveeId, reviverId);
        }

        public static void SendTeamCancelPacket(string reviveeId, string reviverId)
        {
            if (!Plugin.FikaInstalled) return;

            Plugin.LogSource.LogDebug($"Sending team cancel packet: {reviverId} cancelled helping {reviveeId}");
            FikaMethods.SendTeamCancelPacket(reviveeId, reviverId);
        }

        public static void SendSelfReviveStartPacket(string playerId)
        {
            if (!Plugin.FikaInstalled) return;

            Plugin.LogSource.LogDebug($"Sending self revive start packet for {playerId}");
            FikaMethods.SendSelfReviveStartPacket(playerId);
        }

        public static void SendTeamReviveStartPacket(string reviveeId, string reviverId)
        {
            if (!Plugin.FikaInstalled) return;

            Plugin.LogSource.LogDebug($"Sending team revive start packet: {reviverId} reviving {reviveeId}");
            FikaMethods.SendTeamReviveStartPacket(reviveeId, reviverId);
        }

        public static void SendRevivedPacket(string playerId, string reviverId = "")
        {
            if (!Plugin.FikaInstalled) return;

            Plugin.LogSource.LogDebug($"Sending revived packet for {playerId}");
            FikaMethods.SendRevivedPacket(playerId, reviverId);
        }

        public static void SendPlayerStateResetPacket(string playerId, bool isDead, float cooldownSeconds = 0f)
        {
            if (!Plugin.FikaInstalled) return;

            Plugin.LogSource.LogDebug($"Sending state reset packet for {playerId} (isDead={isDead}, cooldown={cooldownSeconds:F0}s)");
            FikaMethods.SendPlayerStateResetPacket(playerId, isDead, cooldownSeconds);
        }

        //====================[ Team Healing Packet Wrappers ]====================
        public static void SendTeamHealPacket(string patientId, string healerId)
        {
            if (!Plugin.FikaInstalled) return;

            Plugin.LogSource.LogDebug($"Sending team heal packet: {healerId} healing {patientId}");
            FikaMethods.SendTeamHealPacket(patientId, healerId);
        }

        public static void SendTeamHealCompletePacket(string patientId, string healerId)
        {
            if (!Plugin.FikaInstalled) return;

            Plugin.LogSource.LogDebug($"Sending team heal complete packet: {healerId} healed {patientId}");
            FikaMethods.SendTeamHealCompletePacket(patientId, healerId);
        }

        public static void SendTeamHealCancelPacket(string patientId, string healerId)
        {
            if (!Plugin.FikaInstalled) return;

            Plugin.LogSource.LogDebug($"Sending team heal cancel packet: {healerId} cancelled healing {patientId}");
            FikaMethods.SendTeamHealCancelPacket(patientId, healerId);
        }
    }
}