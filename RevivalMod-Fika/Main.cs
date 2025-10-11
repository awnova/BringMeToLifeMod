﻿using Comfort.Common;
using Fika.Core.Coop.Utils;
using Fika.Core.Networking;
using RevivalMod.FikaModule.Common;
using RevivalMod.Fika;

namespace RevivalMod.FikaModule
{
    internal class Main
    {
        // called by the core dll via reflection
        public static void Init()
        {
            PluginAwake();
            FikaBridge.PluginEnableEmitted += PluginEnable;

            FikaBridge.IAmHostEmitted += IAmHost;
            FikaBridge.GetRaidIdEmitted += GetRaidId;

            FikaBridge.SendPlayerPositionPacketEmitted += FikaMethods.SendPlayerPositionPacket;
            FikaBridge.SendRemovePlayerFromCriticalPlayersListPacketEmitted += FikaMethods.SendRemovePlayerFromCriticalPlayersListPacket;
            FikaBridge.SendReviveMePacketEmitted += FikaMethods.SendReviveMePacket;
            FikaBridge.SendReviveStartedPacketEmitted += FikaMethods.SendReviveStartedPacket;
            FikaBridge.SendReviveCanceledPacketEmitted += FikaMethods.SendReviveCanceledPacket;
        }

        public static void PluginAwake()
        {
        }

        public static void PluginEnable()
        {
            FikaMethods.InitOnPluginEnabled();
        }

        public static bool IAmHost()
        {
            return Singleton<FikaServer>.Instantiated;
        }

        public static string GetRaidId()
        {
            return FikaBackendUtils.GroupId;
        }
    }
}