//====================[ Imports ]====================
using Fika.Core.Main.Utils;
using KeepMeAlive.Helpers;

namespace KeepMeAlive.Fika
{
    //====================[ FikaBridge ]====================
    internal class FikaBridge
    {
        //====================[ Lifecycle ]====================
        public static void PluginEnable()
        {
            FikaMethods.InitOnPluginEnabled();
            Plugin.LogSource.LogInfo("Fika integration initialized!");
        }

        //====================[ Network Utilities ]====================
        public static bool IAmHost()
        {
            return FikaBackendUtils.IsServer;
        }

        public static string GetRaidId()
        {
            return FikaBackendUtils.GroupId;
        }

        //====================[ Revival Packet Wrappers ]====================
        public static void SendBleedingOutPacket(string playerId, float timeRemaining)
        {
            RevivalDebugLog.LogNetworkTrace($"Sending bleeding out packet for {playerId}");
            FikaMethods.SendBleedingOutPacket(playerId, timeRemaining);
        }

        public static void SendTeamHelpPacket(string reviveeId, string reviverId)
        {
            RevivalDebugLog.LogNetworkTrace($"Sending team help packet: {reviverId} helping {reviveeId}");
            FikaMethods.SendTeamHelpPacket(reviveeId, reviverId);
        }

        public static void SendTeamCancelPacket(string reviveeId, string reviverId)
        {
            RevivalDebugLog.LogNetworkTrace($"Sending team cancel packet: {reviverId} cancelled helping {reviveeId}");
            FikaMethods.SendTeamCancelPacket(reviveeId, reviverId);
        }

        public static void SendSelfReviveStartPacket(string playerId)
        {
            RevivalDebugLog.LogNetworkTrace($"Sending self revive start packet for {playerId}");
            FikaMethods.SendSelfReviveStartPacket(playerId);
        }

        public static void SendTeamReviveStartPacket(string reviveeId, string reviverId)
        {
            RevivalDebugLog.LogNetworkTrace($"Sending team revive start packet: {reviverId} reviving {reviveeId}");
            FikaMethods.SendTeamReviveStartPacket(reviveeId, reviverId);
        }

        public static void SendRevivedPacket(string playerId, string reviverId = "")
        {
            RevivalDebugLog.LogNetworkTrace($"Sending revived packet for {playerId}");
            FikaMethods.SendRevivedPacket(playerId, reviverId);
        }

        public static void SendPlayerStateResetPacket(string playerId, bool isDead, float cooldownSeconds = 0f)
        {
            RevivalDebugLog.LogNetworkTrace($"Sending state reset packet for {playerId} (isDead={isDead}, cooldown={cooldownSeconds:F0}s)");
            FikaMethods.SendPlayerStateResetPacket(playerId, isDead, cooldownSeconds);
        }

        //====================[ Team Healing Packet Wrappers ]====================
        public static void SendTeamHealPacket(string patientId, string healerId, string itemId)
        {
            RevivalDebugLog.LogNetworkTrace($"Sending team heal packet: {healerId} healing {patientId} with {itemId}");
            FikaMethods.SendTeamHealPacket(patientId, healerId, itemId);
        }

        public static void SendTeamHealCancelPacket(string patientId, string healerId)
        {
            RevivalDebugLog.LogNetworkTrace($"Sending team heal cancel packet: {healerId} cancelled healing {patientId}");
            FikaMethods.SendTeamHealCancelPacket(patientId, healerId);
        }

        //====================[ Periodic State Resync ]====================
        // Broadcasts local player's full revival state to all peers. Called periodically while active, and immediately on transitions.
        public static void SendPlayerStateResyncPacket(string playerId, Components.RMPlayer st)
        {
            FikaMethods.SendPlayerStateResyncPacket(playerId, st);
        }
    }
}