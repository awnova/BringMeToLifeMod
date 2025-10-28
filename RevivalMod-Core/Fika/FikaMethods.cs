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

        public static void SendHealthRestoredPacket(string playerId)
        {
            HealthRestoredPacket packet = new()
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

        private static void OnPlayerCriticalStatePacketReceived(PlayerCriticalStatePacket packet, NetPeer peer)
        {
            if (FikaBackendUtils.IsServer && FikaBackendUtils.IsHeadless)
            {
                SendPlayerCriticalStatePacket(packet.playerId);
            }
            else
            {
                // Update RMSession (single source of truth) for network-synced critical state
                // AddToCriticalPlayers now updates both PlayerStates and CriticalPlayers HashSet
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
                // RemovePlayerFromCriticalPlayers now updates both PlayerStates and CriticalPlayers HashSet
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
                
                // Update timer to show the CMS animation duration at CENTER
                if (FikaBackendUtils.Profile.ProfileId == packet.reviveeId)
                {
                    float animDuration = RevivalModSettings.TEAMMATE_REVIVE_ANIMATION_DURATION.Value;
                    var playerState = RMSession.GetPlayerState(packet.reviveeId);
                    
                    // Close the bottom "Being revived" countdown panel
                    Player player = Singleton<GameWorld>.Instance.MainPlayer;
                    if (player.GetComponentInParent<GamePlayerOwner>() is GamePlayerOwner owner)
                    {
                        owner.CloseObjectivesPanel();
                    }
                    
                    // Start center "Reviving" countdown with red-to-green color (healing in progress)
                    playerState.CriticalStateMainTimer?.StartCountdown(
                        animDuration, 
                        "Reviving", 
                        TimerPosition.MiddleCenter,
                        TimerColorMode.RedToGreen
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
                VFX_UI.ShowTeammateNotification("Revival initiated - teammate is reviving...");
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
                
                // Set flag to freeze movement (handled by update loop)
                var playerState = RMSession.GetPlayerState(packet.reviveeId);
                playerState.IsBeingRevived = true;
                
                // Stop the center "Bleeding Out" timer
                playerState.CriticalStateMainTimer?.StopTimer();
                
                // Close the "HOLD F TO REVIVE" prompt and replace with 2-second countdown at BOTTOM
                Player player = Singleton<GameWorld>.Instance.MainPlayer;
                if (player.GetComponentInParent<GamePlayerOwner>() is GamePlayerOwner owner)
                {
                    const float REVIVE_HOLD_TIME = 2f;
                    owner.ShowObjectivesPanel("Being revived {0:F1}", REVIVE_HOLD_TIME);
                    
                    // Color the objectives panel blue
                    VFX_UI.ColorObjectivesPanelBlue();
                }
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

                // Clear flag and restore crawl speed (recalculate from config)
                var playerState = RMSession.GetPlayerState(packet.reviveeId);
                playerState.IsBeingRevived = false;

                // Close the bottom "Being revived" countdown panel
                Player player = Singleton<GameWorld>.Instance.MainPlayer;
                if (player.GetComponentInParent<GamePlayerOwner>() is GamePlayerOwner owner)
                {
                    owner.CloseObjectivesPanel();
                    
                    // Restore the "HOLD F TO REVIVE" prompt if player has defib
                    if (RevivalModSettings.SELF_REVIVAL_ENABLED.Value && Utils.HasDefib(player))
                    {
                        owner.ShowObjectivesPanel($"HOLD [{RevivalModSettings.SELF_REVIVAL_KEY.Value}] TO REVIVE", 999999f);
                    }
                }

                // Resume the "Bleeding Out" critical state timer with red-to-black transition at CENTER
                playerState.CriticalStateMainTimer?.StartCountdown(
                    playerState.CriticalTimer,
                    "Bleeding Out", 
                    TimerPosition.MiddleCenter,
                    TimerColorMode.RedToBlack
                );
            }
        }

        private static void OnHealthRestoredPacketReceived(HealthRestoredPacket packet, NetPeer peer)
        {
            if (FikaBackendUtils.IsServer && FikaBackendUtils.IsHeadless)
            {
                SendHealthRestoredPacket(packet.playerId);
            }
            else
            {
                // Don't apply to local player - they already have the restoration
                if (FikaBackendUtils.Profile.ProfileId == packet.playerId)
                    return;

                Plugin.LogSource.LogDebug($"HealthRestored packet received for player {packet.playerId}");

                // Find the player and restore their body parts
                Player player = Utils.GetPlayerById(packet.playerId);
                if (player != null)
                {
                    // Pass false to prevent sending another packet (avoid infinite loop)
                    BodyPartRestoration.RestoreDestroyedBodyParts(player, sendNetworkPacket: false);
                    Plugin.LogSource.LogInfo($"Synced body part restoration for {packet.playerId}");
                }
                else
                {
                    Plugin.LogSource.LogWarning($"Could not find player {packet.playerId} to sync health restoration");
                }
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
            managerCreatedEvent.Manager.RegisterPacket<HealthRestoredPacket, NetPeer>(OnHealthRestoredPacketReceived);
        }
        
        public static void InitOnPluginEnabled()
        {
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnFikaNetManagerCreated);
        }
    }
}
