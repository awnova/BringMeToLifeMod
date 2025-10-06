﻿using SPT.Reflection.Utils;
using System;
using UnityEngine;

namespace RevivalMod.Fika
{
    internal class FikaBridge
    {
        public delegate void SimpleEvent();
        public delegate bool SimpleBoolReturnEvent();
        public delegate string SimpleStringReturnEvent();

        public static event SimpleEvent PluginEnableEmitted;
        public static void PluginEnable()
        {
            PluginEnableEmitted?.Invoke(); 
            
            if (PluginEnableEmitted != null)
            {
                Plugin.LogSource.LogInfo("RevivalMod-Fika plugin loaded!");
            }
        }
        public static event SimpleBoolReturnEvent IAmHostEmitted;
        public static bool IAmHost()
        {
            bool? eventResponse = IAmHostEmitted?.Invoke();

            if (eventResponse == null)
            {
                return true;
            }
            else
            {
                return eventResponse.Value;
            }
        }
        public static event SimpleStringReturnEvent GetRaidIdEmitted;
        public static string GetRaidId()
        {
            string eventResponse = GetRaidIdEmitted?.Invoke();

            if (eventResponse == null)
            {
                return ClientAppUtils.GetMainApp().GetClientBackEndSession().Profile.ProfileId;
            }
            else
            {
                return eventResponse;
            }
        }

        public delegate void SendPlayerPositionPacketEvent(string playerId, DateTime timeOfDeath, Vector3 position);
        public static event SendPlayerPositionPacketEvent SendPlayerPositionPacketEmitted;
        public static void SendPlayerPositionPacket(string playerId, DateTime timeOfDeath, Vector3 position)
        { 
            Plugin.LogSource.LogDebug("Sending player position packet");
            SendPlayerPositionPacketEmitted?.Invoke(playerId, timeOfDeath, position); 
        }

        public delegate void SendRemovePlayerFromCriticalPlayersListPacketEvent(string playerId);
        public static event SendRemovePlayerFromCriticalPlayersListPacketEvent SendRemovePlayerFromCriticalPlayersListPacketEmitted;
        public static void SendRemovePlayerFromCriticalPlayersListPacket(string playerId)
        {
            Plugin.LogSource.LogDebug("Sending remove player from critical players list packet");
            SendRemovePlayerFromCriticalPlayersListPacketEmitted?.Invoke(playerId); 
        }

        public delegate void SendReviveMePacketEvent(string reviveeId, string reviverId);
        public static event SendReviveMePacketEvent SendReviveMePacketEmitted;
        public static void SendReviveMePacket(string reviveeId, string reviverId)
        {
            Plugin.LogSource.LogDebug("Sending revive me packet");
            SendReviveMePacketEmitted?.Invoke(reviveeId, reviverId); 
        }

        //public delegate void SendRevivedPacketEvent(string reviverId, NetPeer peer);
        //public static event SendRevivedPacketEvent SendRevivedPacketEmitted;
        //public static void SendRevivedPacket(string reviverId, NetPeer peer)
        //{
        //    Plugin.LogSource.LogInfo("Sending revived packet");
        //    SendRevivedPacketEmitted?.Invoke(reviverId, peer); 
        //}
    }
}