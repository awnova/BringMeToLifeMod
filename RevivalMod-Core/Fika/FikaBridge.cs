using SPT.Reflection.Utils;
using System;
using UnityEngine;
using Comfort.Common;
using Fika.Core.Coop.Utils;
using Fika.Core.Networking;

namespace RevivalMod.Fika
{
    internal class FikaBridge
    {
        public static void PluginEnable()
        {
            if (!Plugin.FikaInstalled)
                return;

            FikaMethods.InitOnPluginEnabled();
            Plugin.LogSource.LogInfo("Fika integration initialized!");
        }

        public static bool IAmHost()
        {
            if (!Plugin.FikaInstalled)
                return true;

            return Singleton<FikaServer>.Instantiated;
        }

        public static string GetRaidId()
        {
            if (!Plugin.FikaInstalled)
                return ClientAppUtils.GetMainApp().GetClientBackEndSession().Profile.ProfileId;

            return FikaBackendUtils.GroupId;
        }

        public static void SendPlayerCriticalStatePacket(string playerId)
        {
            if (!Plugin.FikaInstalled)
                return;

            FikaMethods.SendPlayerCriticalStatePacket(playerId);
        }

        public static void SendRemovePlayerFromCriticalPlayersListPacket(string playerId)
        {
            if (!Plugin.FikaInstalled)
                return;

            Plugin.LogSource.LogDebug("Sending remove player from critical players list packet");
            FikaMethods.SendRemovePlayerFromCriticalPlayersListPacket(playerId);
        }

        public static void SendReviveMePacket(string reviveeId, string reviverId)
        {
            if (!Plugin.FikaInstalled)
                return;

            Plugin.LogSource.LogDebug("Sending revive me packet");
            FikaMethods.SendReviveMePacket(reviveeId, reviverId);
        }

        public static void SendReviveStartedPacket(string reviveeId, string reviverId)
        {
            if (!Plugin.FikaInstalled)
                return;

            Plugin.LogSource.LogDebug("Sending revive started packet");
            FikaMethods.SendReviveStartedPacket(reviveeId, reviverId);
        }

        public static void SendReviveCanceledPacket(string reviveeId, string reviverId)
        {
            if (!Plugin.FikaInstalled)
                return;

            Plugin.LogSource.LogDebug("Sending revive canceled packet");
            FikaMethods.SendReviveCanceledPacket(reviveeId, reviverId);
        }

        public static void SendHealthRestoredPacket(string playerId)
        {
            if (!Plugin.FikaInstalled)
                return;

            Plugin.LogSource.LogDebug($"Sending health restored packet for player {playerId}");
            FikaMethods.SendHealthRestoredPacket(playerId);
        }
    }
}