using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.UI;
using Fika.Core.Coop.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using LiteNetLib;
using RevivalMod.Components;
using RevivalMod.Features;
using RevivalMod.Fika.Packets;
using RevivalMod.Helpers;
using System;
using UnityEngine;
using TMPro;

namespace RevivalMod.Fika
{
    internal class FikaMethods
    {
        public static void SendPlayerCriticalStatePacket(string playerId)
        {
            PlayerCriticalStatePacket packet = new()
            {
                playerId = playerId
            };

            if (FikaBackendUtils.IsServer)
            {
                try
                {
                    Singleton<FikaServer>.Instance.SendDataToAll(ref packet, DeliveryMethod.ReliableOrdered);
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError(ex);
                }
            }
            else if (FikaBackendUtils.IsClient)
            {
                Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableSequenced);
            }

        }
                
        public static void SendRemovePlayerFromCriticalPlayersListPacket(string playerId)
        {
            RemovePlayerFromCriticalPlayersListPacket packet = new()
            {
                playerId = playerId
            };

            if (FikaBackendUtils.IsServer)
            {
                try
                {
                    Singleton<FikaServer>.Instance.SendDataToAll(ref packet, DeliveryMethod.ReliableOrdered);
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError(ex);
                }
            }
            else if (FikaBackendUtils.IsClient)
            {
                Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableSequenced);
            }
        }

        public static void SendReviveMePacket(string reviveeId, string reviverId)
        {
            ReviveMePacket packet = new()
            {
                reviverId = reviverId,
                reviveeId = reviveeId
            };

            if (FikaBackendUtils.IsServer)
            {               
                try
                {
                    Singleton<FikaServer>.Instance.SendDataToAll(ref packet, DeliveryMethod.ReliableOrdered);
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError(ex);
                }
            }
            else if (FikaBackendUtils.IsClient)
            {              
                Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableSequenced);
            }
        }

        public static void SendReviveSucceedPacket(string reviverId, NetPeer peer)
        {
            RevivedPacket packet = new()
            {
                reviverId = reviverId
            };

            if (FikaBackendUtils.IsServer)
            {
                try
                {
                    Singleton<FikaServer>.Instance.SendDataToPeer(peer, ref packet, DeliveryMethod.ReliableOrdered);
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError(ex);
                }
            }
            else if (FikaBackendUtils.IsClient)
            {
                Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableSequenced);
            }

        }

        public static void SendReviveStartedPacket(string reviveeId, string reviverId)
        {
            ReviveStartedPacket packet = new()
            {
                reviverId = reviverId,
                reviveeId = reviveeId
            };

            if (FikaBackendUtils.IsServer)
            {
                try
                {
                    Singleton<FikaServer>.Instance.SendDataToAll(ref packet, DeliveryMethod.ReliableOrdered);
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError(ex);
                }
            }
            else if (FikaBackendUtils.IsClient)
            {
                Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableSequenced);
            }
        }
        
        public static void SendReviveCanceledPacket(string reviveeId, string reviverId)
        {
            ReviveCanceledPacket packet = new()
            {
                reviverId = reviverId,
                reviveeId = reviveeId
            };

            if (FikaBackendUtils.IsServer)
            {               
                try
                {
                    Singleton<FikaServer>.Instance.SendDataToAll(ref packet, DeliveryMethod.ReliableOrdered);
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError(ex);
                }
            }
            else if (FikaBackendUtils.IsClient)
            {
                Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableSequenced);
            }
        }

        private static void OnPlayerCriticalStatePacketReceived(PlayerCriticalStatePacket packet, NetPeer peer)
        {
            if (FikaBackendUtils.IsServer && FikaBackendUtils.IsHeadless)
            {
                SendPlayerCriticalStatePacket(packet.playerId);
            }
            else
            {
                // Update RMSession (single source of truth) for network-synced critical state
                var playerState = RMSession.GetPlayerState(packet.playerId);
                playerState.IsCritical = true;
                RMSession.AddToCriticalPlayers(packet.playerId);

                // If this process actually runs AI, also apply ghost-mode removal locally
                try
                {
                    var bots = UnityEngine.Object.FindObjectsOfType<BotOwner>();
                    if (bots != null && bots.Length > 0)
                    {
                        RevivalMod.Patches.GhostModeEnemyManager.EnterGhostModeById(packet.playerId);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError(ex);
                }
            }
        }
        
        private static void OnRemovePlayerFromCriticalPlayersListPacketReceived(RemovePlayerFromCriticalPlayersListPacket packet, NetPeer peer)
        {
            if (FikaBackendUtils.IsServer && FikaBackendUtils.IsHeadless)
            {
                SendRemovePlayerFromCriticalPlayersListPacket(packet.playerId);
            }
            else
            {
                // Update RMSession (single source of truth) for network-synced critical state removal
                if (RMSession.HasPlayerState(packet.playerId))
                {
                    RMSession.GetPlayerState(packet.playerId).IsCritical = false;
                }
                RMSession.RemovePlayerFromCriticalPlayers(packet.playerId);

                // If this process runs AI, restore the player in the local enemy lists
                try
                {
                    var bots = UnityEngine.Object.FindObjectsOfType<BotOwner>();
                    if (bots != null && bots.Length > 0)
                    {
                        RevivalMod.Patches.GhostModeEnemyManager.ExitGhostModeById(packet.playerId);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError(ex);
                }
            }
        }
        
        /// <summary>
        /// Handles the reception of a <see cref="ReviveMePacket"/> from a network peer.
        /// Depending on the server state and backend configuration, either forwards the revive request
        /// or attempts to perform a revival by a teammate. If the revival is successful, sends a notification
        /// packet to the reviver.
        /// </summary>
        /// <param name="packet">The <see cref="ReviveMePacket"/> containing revivee and reviver IDs.</param>
        /// <param name="peer">The <see cref="NetPeer"/> that sent the packet.</param>
        private static void OnReviveMePacketReceived(ReviveMePacket packet, NetPeer peer)
        {
            if (FikaBackendUtils.IsServer && FikaBackendUtils.IsHeadless)
            {
                SendReviveMePacket(packet.reviveeId, packet.reviverId);
            }
            else
            {
                // This packet is received after teammate finished holding (2s)
                // Now start the CMS animation on the revivee
                bool animationStarted = RevivalFeatures.TryPerformRevivalByTeammate(packet.reviveeId);
                
                if (!animationStarted) 
                    return;
                
                // Update timer to show the CMS animation duration
                if (FikaBackendUtils.Profile.ProfileId == packet.reviveeId)
                {
                    float animDuration = RevivalModSettings.TEAMMATE_REVIVE_ANIMATION_DURATION.Value;
                    
                    // Transition from "Being revived" countdown to "Reviving" countdown with red-to-green color
                    RevivalFeatures.criticalStateMainTimer?.StartCountdown(
                        animDuration, 
                        "Reviving", 
                        TimerPosition.MiddleCenter,
                        TimerColorMode.RedToGreen  // Healing in progress - red to green
                    );
                }
                
                // Notify the reviver that revival animation has started
                SendReviveSucceedPacket(packet.reviverId, peer);
            }
        }

        private static void OnReviveSucceedPacketReceived(RevivedPacket packet, NetPeer peer)
        {
            if (FikaBackendUtils.IsServer && FikaBackendUtils.IsHeadless)
            {
                SendReviveSucceedPacket(packet.reviverId, peer);
            }
            else
            {
                // Reviver receives confirmation that revival process started successfully
                NotificationManagerClass.DisplayMessageNotification(
                        $"Revival initiated - teammate is reviving...",
                        ENotificationDurationType.Long,
                        ENotificationIconType.Friend,
                        Color.green);
            }
        }

        private static void OnReviveStartedPacketReceived(ReviveStartedPacket packet, NetPeer peer)
        {
            if (FikaBackendUtils.IsServer && FikaBackendUtils.IsHeadless)
            {
                SendReviveStartedPacket(packet.reviveeId, packet.reviverId);
            }
            else
            {
                if (FikaBackendUtils.Profile.ProfileId != packet.reviveeId)
                    return;

                Plugin.LogSource.LogDebug("ReviveStarted packet received - teammate is holding to revive");
                
                // Show a 2-second countdown while teammate is holding to initiate revival
                const float REVIVE_HOLD_TIME = 2f;
                RevivalFeatures.criticalStateMainTimer?.StartCountdown(
                    REVIVE_HOLD_TIME, 
                    "Being revived", 
                    TimerPosition.MiddleCenter,
                    TimerColorMode.StaticBlue  // Blue color for short "being revived" state
                );
            }
        }

        private static void OnReviveCanceledPacketReceived(ReviveCanceledPacket packet, NetPeer peer)
        {
            if (FikaBackendUtils.IsServer && FikaBackendUtils.IsHeadless)
            {
                SendReviveCanceledPacket(packet.reviveeId, packet.reviverId);
            }
            else
            {
                if (FikaBackendUtils.Profile.ProfileId != packet.reviveeId)
                    return;
                    
                Plugin.LogSource.LogDebug("ReviveCanceled packet received - teammate stopped holding");

                // Resume the "Bleeding Out" critical state timer with red-to-black transition
                var playerState = RMSession.GetPlayerState(packet.reviveeId);
                RevivalFeatures.criticalStateMainTimer?.StartCountdown(
                    playerState.CriticalTimer,
                    "Bleeding Out", 
                    TimerPosition.MiddleCenter,
                    TimerColorMode.RedToBlack
                );
            }
        }

        public static void OnFikaNetManagerCreated(FikaNetworkManagerCreatedEvent managerCreatedEvent)
        {
            managerCreatedEvent.Manager.RegisterPacket<PlayerCriticalStatePacket, NetPeer>(OnPlayerCriticalStatePacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<RemovePlayerFromCriticalPlayersListPacket, NetPeer>(OnRemovePlayerFromCriticalPlayersListPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<ReviveMePacket, NetPeer>(OnReviveMePacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<RevivedPacket, NetPeer>(OnReviveSucceedPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<ReviveStartedPacket, NetPeer>(OnReviveStartedPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<ReviveCanceledPacket, NetPeer>(OnReviveCanceledPacketReceived);
        }
        
        public static void InitOnPluginEnabled()
        {
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnFikaNetManagerCreated);
        }
    }
}
