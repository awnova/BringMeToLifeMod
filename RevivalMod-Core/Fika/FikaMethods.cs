//====================[ Imports ]====================
using System;
using Comfort.Common;
using EFT;
using Fika.Core.Coop.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using LiteNetLib;
using LiteNetLib.Utils;
using RevivalMod.Components;        // <-- provides RMSession / RMState
using RevivalMod.Fika.Packets;
using RevivalMod.Helpers;
using UnityEngine;

namespace RevivalMod.Fika
{
    //====================[ FikaMethods ]====================
    internal class FikaMethods
    {
        //====================[ Send Helpers ]====================
        private static void SendPacket<T>(ref T packet) where T : struct, INetSerializable
        {
            if (FikaBackendUtils.IsServer)
            {
                try { Singleton<FikaServer>.Instance.SendDataToAll(ref packet, DeliveryMethod.ReliableOrdered); }
                catch (Exception ex) { Plugin.LogSource.LogError(ex); }
            }
            else if (FikaBackendUtils.IsClient)
            {
                try { Singleton<FikaClient>.Instance.SendData(ref packet, DeliveryMethod.ReliableSequenced); }
                catch (Exception ex) { Plugin.LogSource.LogError(ex); }
            }
        }

        // Returns true if relayed and caller should skip local handling.
        private static bool TryRelayIfHeadless<T>(T packet, Action<T> relayAction)
        {
            if (FikaBackendUtils.IsHeadless && FikaBackendUtils.IsServer)
            {
                try { relayAction(packet); }
                catch (Exception ex) { Plugin.LogSource.LogError(ex); }
                return true;
            }
            return false;
        }

        //====================[ Packet Senders ]====================
        public static void SendBleedingOutPacket(string playerId, float timeRemaining)
        {
            BleedingOutPacket packet = new() { playerId = playerId, timeRemaining = timeRemaining };
            SendPacket(ref packet);
        }

        public static void SendTeamHelpPacket(string reviveeId, string reviverId)
        {
            TeamHelpPacket packet = new() { reviveeId = reviveeId, reviverId = reviverId };
            SendPacket(ref packet);
        }

        public static void SendTeamCancelPacket(string reviveeId, string reviverId)
        {
            TeamCancelPacket packet = new() { reviveeId = reviveeId, reviverId = reviverId };
            SendPacket(ref packet);
        }

        public static void SendSelfReviveStartPacket(string playerId)
        {
            SelfReviveStartPacket packet = new() { playerId = playerId };
            SendPacket(ref packet);
        }

        public static void SendTeamReviveStartPacket(string reviveeId, string reviverId)
        {
            TeamReviveStartPacket packet = new() { reviveeId = reviveeId, reviverId = reviverId };
            SendPacket(ref packet);
        }

        public static void SendRevivedPacket(string playerId, string reviverId = "")
        {
            RevivedPacket packet = new() { playerId = playerId, reviverId = reviverId };
            SendPacket(ref packet);
        }

        public static void SendPlayerStateResetPacket(string playerId)
        {
            PlayerStateResetPacket packet = new() { playerId = playerId };
            SendPacket(ref packet);
        }

        //====================[ Packet Receivers ]====================
        private static void OnBleedingOutPacketReceived(BleedingOutPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendBleedingOutPacket(p.playerId, p.timeRemaining))) return;

            var playerState = RMSession.GetPlayerState(packet.playerId);
            playerState.State = RMState.BleedingOut;
            playerState.CriticalTimer = packet.timeRemaining;

            Plugin.LogSource.LogDebug($"[Packet] BleedingOut: {packet.playerId} has {packet.timeRemaining}s left");

            if (RevivalModSettings.GHOST_MODE.Value || GodMode.IsEnabled())
            {
                Player player = Utils.GetPlayerById(packet.playerId);
                if (player != null && player.ActiveHealthController != null)
                {
                    if (RevivalModSettings.GHOST_MODE.Value) GhostMode.EnterGhostModeById(packet.playerId);
                    if (GodMode.IsEnabled()) GodMode.Enable(player);
                }
            }
        }

        private static void OnTeamHelpPacketReceived(TeamHelpPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendTeamHelpPacket(p.reviveeId, p.reviverId))) return;

            Plugin.LogSource.LogDebug($"[Packet] TeamHelp: {packet.reviverId} started helping {packet.reviveeId}");

            var playerState = RMSession.GetPlayerState(packet.reviveeId);
            playerState.CurrentReviverId = packet.reviverId;
            playerState.IsBeingRevived = true;

            try
            {
                string reviverName = Utils.GetPlayerDisplayName(packet.reviverId);
                string reviveeName = Utils.GetPlayerDisplayName(packet.reviveeId);
                VFX_UI.Text(Color.cyan, $"{reviverName} is helping {reviveeName}");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[VFX_UI] TeamHelp notify failed: {ex.Message}"); }
        }

        private static void OnTeamCancelPacketReceived(TeamCancelPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendTeamCancelPacket(p.reviveeId, p.reviverId))) return;

            Plugin.LogSource.LogDebug($"[Packet] TeamCancel: {packet.reviverId} cancelled helping {packet.reviveeId}");

            var playerState = RMSession.GetPlayerState(packet.reviveeId);
            playerState.State = RMState.BleedingOut;
            playerState.IsBeingRevived = false;

            try
            {
                string reviverName = Utils.GetPlayerDisplayName(packet.reviverId);
                VFX_UI.Text(Color.yellow, $"{reviverName} cancelled revival");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[VFX_UI] TeamCancel notify failed: {ex.Message}"); }
        }

        private static void OnSelfReviveStartPacketReceived(SelfReviveStartPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendSelfReviveStartPacket(p.playerId))) return;

            Plugin.LogSource.LogDebug($"[Packet] SelfReviveStart: {packet.playerId} started self-revival");

            var playerState = RMSession.GetPlayerState(packet.playerId);
            playerState.State = RMState.Reviving;
            playerState.ReviveRequestedSource = 0; // Self
            playerState.RevivalRequested = true;

            RMSession.UpdatePlayerState(packet.playerId, playerState);

            try
            {
                string display = Utils.GetPlayerDisplayName(packet.playerId);
                VFX_UI.Text(Color.cyan, $"{display} is self-reviving");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[VFX_UI] SelfReviveStart notify failed: {ex.Message}"); }
        }

        private static void OnTeamReviveStartPacketReceived(TeamReviveStartPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendTeamReviveStartPacket(p.reviveeId, p.reviverId))) return;

            Plugin.LogSource.LogDebug($"[Packet] TeamReviveStart: {packet.reviverId} started reviving {packet.reviveeId}");

            var playerState = RMSession.GetPlayerState(packet.reviveeId);
            playerState.State = RMState.Reviving;
            playerState.ReviveRequestedSource = 1; // Team
            playerState.RevivalRequested = true;
            playerState.CurrentReviverId = packet.reviverId;

            RMSession.UpdatePlayerState(packet.reviveeId, playerState);

            try
            {
                string reviverName = Utils.GetPlayerDisplayName(packet.reviverId);
                string reviveeName = Utils.GetPlayerDisplayName(packet.reviveeId);
                VFX_UI.Text(Color.cyan, $"{reviverName} is reviving {reviveeName}");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[VFX_UI] TeamReviveStart notify failed: {ex.Message}"); }
        }

        private static void OnRevivedPacketReceived(RevivedPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendRevivedPacket(p.playerId, p.reviverId))) return;

            Plugin.LogSource.LogDebug($"[Packet] Revived: {packet.playerId} was revived by {packet.reviverId}");

            var playerState = RMSession.GetPlayerState(packet.playerId);
            playerState.State = RMState.Revived;
            playerState.InvulnerabilityTimer = RevivalModSettings.REVIVAL_DURATION.Value;

            Player player = Utils.GetPlayerById(packet.playerId);
            if (player != null)
            {
                GodMode.ForceEnable(player);
                GhostMode.ExitGhostMode(player);
            }

            try
            {
                string display = Utils.GetPlayerDisplayName(packet.playerId);
                bool isSelfRevive = string.IsNullOrEmpty(packet.reviverId) || packet.reviverId == packet.playerId;
                VFX_UI.Text(Color.green, isSelfRevive ? $"{display} self-revived" : $"{display} was revived");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[VFX_UI] Revived notify failed: {ex.Message}"); }
        }

        private static void OnPlayerStateResetPacketReceived(PlayerStateResetPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendPlayerStateResetPacket(p.playerId))) return;

            Plugin.LogSource.LogDebug($"[Packet] StateReset: {packet.playerId} returned to normal state");

            var playerState = RMSession.GetPlayerState(packet.playerId);
            playerState.State = RMState.None;
            playerState.InvulnerabilityTimer = 0f;
        }

        //====================[ Registration / Init ]====================
        public static void OnFikaNetManagerCreated(FikaNetworkManagerCreatedEvent managerCreatedEvent)
        {
            managerCreatedEvent.Manager.RegisterPacket<BleedingOutPacket, NetPeer>(OnBleedingOutPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<TeamHelpPacket, NetPeer>(OnTeamHelpPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<TeamCancelPacket, NetPeer>(OnTeamCancelPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<SelfReviveStartPacket, NetPeer>(OnSelfReviveStartPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<TeamReviveStartPacket, NetPeer>(OnTeamReviveStartPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<RevivedPacket, NetPeer>(OnRevivedPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<PlayerStateResetPacket, NetPeer>(OnPlayerStateResetPacketReceived);
        }

        public static void InitOnPluginEnabled() =>
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnFikaNetManagerCreated);

        // HealthRestored packet removed: restoration inferred from state transitions
    }
}
