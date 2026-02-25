//====================[ Imports ]====================
using System;
using Comfort.Common;
using EFT;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
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
            if (!Singleton<IFikaNetworkManager>.Instantiated)
            {
                return;
            }

            try
            {
                bool broadcast = FikaBackendUtils.IsServer;
                Singleton<IFikaNetworkManager>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered, broadcast);
            }
            catch (Exception ex) { Plugin.LogSource.LogError(ex); }
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

        public static void SendPlayerStateResetPacket(string playerId, bool isDead, float cooldownSeconds = 0f)
        {
            PlayerStateResetPacket packet = new() { playerId = playerId, isDead = isDead, cooldownSeconds = cooldownSeconds };
            SendPacket(ref packet);
        }

        //====================[ Team Healing Packet Senders ]====================
        public static void SendTeamHealPacket(string patientId, string healerId)
        {
            TeamHealPacket packet = new() { patientId = patientId, healerId = healerId };
            SendPacket(ref packet);
        }

        public static void SendTeamHealCompletePacket(string patientId, string healerId)
        {
            TeamHealCompletePacket packet = new() { patientId = patientId, healerId = healerId };
            SendPacket(ref packet);
        }

        public static void SendTeamHealCancelPacket(string patientId, string healerId)
        {
            TeamHealCancelPacket packet = new() { patientId = patientId, healerId = healerId };
            SendPacket(ref packet);
        }

        public static void SendPlayerStateResyncPacket(string playerId, RMPlayer st)
        {
            PlayerStateResyncPacket packet = new()
            {
                playerId      = playerId,
                state         = (int)st.State,
                criticalTimer = st.CriticalTimer,
                invulTimer    = st.InvulnerabilityTimer,
                cooldownTimer = st.CooldownTimer,
                reviverId     = st.CurrentReviverId ?? ""
            };
            SendPacket(ref packet);
        }

        //====================[ Packet Receivers ]====================
        private static void OnBleedingOutPacketReceived(BleedingOutPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendBleedingOutPacket(p.playerId, p.timeRemaining))) return;

            var playerState = RMSession.GetPlayerState(packet.playerId);
            playerState.State = RMState.BleedingOut;
            playerState.CriticalTimer = packet.timeRemaining;
            
            // Reset kill override to ensure death blocking works for remote players
            playerState.KillOverride = false;
            
            // Ensure player is tracked in critical players list for death blocking
            RMSession.AddToCriticalPlayers(packet.playerId);

            Plugin.LogSource.LogDebug($"[Packet] BleedingOut: {packet.playerId} has {packet.timeRemaining}s left (remote player)");

            Player player = Utils.GetPlayerById(packet.playerId);
            if (player != null && player.ActiveHealthController != null)
            {
                MedicalAnimations.EnsureFakeItemsForRemotePlayer(player);
                // Apply protections for remote players to prevent death
                if (RevivalModSettings.GHOST_MODE.Value) GhostMode.EnterGhostModeById(packet.playerId);
                if (RevivalModSettings.GOD_MODE.Value) GodMode.Enable(player);
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

                // If we are the revivee, replace the self-revive prompt with a "being revived" indicator
                Player reviveePlayer = Utils.GetPlayerById(packet.reviveeId);
                if (reviveePlayer != null && reviveePlayer.IsYourPlayer)
                {
                    // Cancel any in-progress self-revive key hold so the KeyUp/KeyHeld
                    // checks in HandleSelfRevival_RequestSession won't fire and corrupt
                    // our IsBeingRevived state.
                    playerState.SelfRevivalKeyHoldDuration.Clear();

                    VFX_UI.HideObjectivePanel();
                    playerState.RevivePromptTimer?.Stop();
                    playerState.RevivePromptTimer = null;
                    VFX_UI.ObjectivePanel(Color.cyan, VFX_UI.Position.BottomCenter,
                        $"{reviverName} is reviving you...");
                }
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
            playerState.CurrentReviverId = string.Empty;

            // If we are the revivee, restore the self-revive prompt
            Player reviveePlayer = Utils.GetPlayerById(packet.reviveeId);
            if (reviveePlayer != null && reviveePlayer.IsYourPlayer)
            {
                VFX_UI.HideObjectivePanel();
                playerState.RevivePromptTimer?.Stop();
                playerState.RevivePromptTimer = null;

                if (RevivalModSettings.SELF_REVIVAL_ENABLED.Value && Utils.HasDefib(reviveePlayer))
                {
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter,
                        $"Revive! [{RevivalModSettings.SELF_REVIVAL_KEY.Value}]");
                }
            }

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

            // Ensure fake items exist now — ApplyItem fires immediately on the
            // reviving player's client, sending a health-sync packet that observers
            // must be able to resolve by item ID.
            var selfRevivePlayer = Utils.GetPlayerById(packet.playerId);
            if (selfRevivePlayer != null) MedicalAnimations.EnsureFakeItemsForRemotePlayer(selfRevivePlayer);

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

            // Ensure fake items exist now — ApplyItem fires immediately on the
            // reviving player's client, sending a health-sync packet that observers
            // must be able to resolve by item ID.
            var teamReviveePlayer = Utils.GetPlayerById(packet.reviveeId);
            if (teamReviveePlayer != null) MedicalAnimations.EnsureFakeItemsForRemotePlayer(teamReviveePlayer);

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

            Player player = Utils.GetPlayerById(packet.playerId);
            if (player == null)
            {
                Plugin.LogSource.LogWarning($"[Packet] Revived: Player {packet.playerId} not found");
                return;
            }

            var playerState = RMSession.GetPlayerState(packet.playerId);
            playerState.State = RMState.Revived;
            playerState.InvulnerabilityTimer = RevivalModSettings.REVIVAL_DURATION.Value;
            playerState.KillOverride = false;
            
            // Remove from critical players list (they're now revived, not critical)
            RMSession.RemovePlayerFromCriticalPlayers(packet.playerId);

            // Clean up fake items used for revival animations
            MedicalAnimations.CleanupFakeItemsForRemotePlayer(player);

            // Enable protections
            GodMode.ForceEnable(player);
            GhostMode.ExitGhostMode(player);

            // Restore body health (for remote clients)
            try
            {
                PlayerRestorations.RestoreDestroyedBodyParts(player);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[Packet] RestoreDestroyedBodyParts error: {ex.Message}");
            }

            // Store original movement speed if not already stored (for remote players)
            try
            {
                if (playerState.OriginalMovementSpeed < 0)
                {
                    playerState.OriginalMovementSpeed = player.Physical.WalkSpeedLimit;
                    Plugin.LogSource.LogDebug($"[Packet] Stored original movement speed for {packet.playerId}: {playerState.OriginalMovementSpeed}");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[Packet] Store movement speed error: {ex.Message}");
            }

            // Restore movement - same as StartInvulnerabilityPeriod
            try
            {
                if (playerState.OriginalMovementSpeed > 0)
                {
                    player.Physical.WalkSpeedLimit = playerState.OriginalMovementSpeed;
                }

                if (player.MovementContext != null)
                {
                    player.MovementContext.IsInPronePose = false;
                    player.MovementContext.EnableSprint(true);
                    player.MovementContext.SetPoseLevel(1f); // Stand up
                }

                Plugin.LogSource.LogDebug($"[Packet] Restored movement for revived player {packet.playerId} (speed: {playerState.OriginalMovementSpeed})");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[Packet] Movement restoration error: {ex.Message}");
            }

            // Update UI for local player
            if (player.IsYourPlayer)
            {
                try
                {
                    VFX_UI.HideTransitPanel();
                    playerState.CriticalStateMainTimer?.Stop();
                    playerState.CriticalStateMainTimer = null;

                    float dur = RevivalModSettings.REVIVAL_DURATION.Value;
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, "Invulnerable {0:F1}", dur);
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogWarning($"[Packet] UI update error: {ex.Message}");
                }
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
            if (TryRelayIfHeadless(packet, p => SendPlayerStateResetPacket(p.playerId, p.isDead, p.cooldownSeconds))) return;

            var playerState = RMSession.GetPlayerState(packet.playerId);
            RMSession.RemovePlayerFromCriticalPlayers(packet.playerId);

            if (packet.isDead)
            {
                Plugin.LogSource.LogDebug($"[Packet] StateReset: {packet.playerId} died");
                playerState.State = RMState.None;
                playerState.KillOverride = true;
                playerState.InvulnerabilityTimer = 0f;
                playerState.CriticalTimer = 0f;
            }
            else
            {
                Plugin.LogSource.LogDebug($"[Packet] StateReset: {packet.playerId} entered cooldown ({packet.cooldownSeconds:F0}s)");
                playerState.State = RMState.CoolDown;
                playerState.CooldownTimer = packet.cooldownSeconds;
                playerState.KillOverride = false;
                playerState.InvulnerabilityTimer = 0f;
                playerState.CriticalTimer = 0f;
            }

            Player player = Utils.GetPlayerById(packet.playerId);
            if (player != null)
            {
                MedicalAnimations.CleanupFakeItemsForRemotePlayer(player);
                if (RevivalModSettings.GHOST_MODE.Value) GhostMode.ExitGhostMode(player);
                GodMode.Disable(player);

                if (packet.isDead)
                    player.ActiveHealthController?.Kill(EDamageType.Undefined);
            }
        }

        //====================[ Team Healing Packet Receivers ]====================
        private static void OnTeamHealPacketReceived(TeamHealPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendTeamHealPacket(p.patientId, p.healerId))) return;

            Plugin.LogSource.LogDebug($"[Packet] TeamHeal: {packet.healerId} started healing {packet.patientId}");

            try
            {
                string healerDisplay = Utils.GetPlayerDisplayName(packet.healerId);
                string patientDisplay = Utils.GetPlayerDisplayName(packet.patientId);
                VFX_UI.Text(Color.green, $"{healerDisplay} is healing {patientDisplay}");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[VFX_UI] TeamHeal notify failed: {ex.Message}"); }
        }

        private static void OnTeamHealCompletePacketReceived(TeamHealCompletePacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendTeamHealCompletePacket(p.patientId, p.healerId))) return;

            Plugin.LogSource.LogDebug($"[Packet] TeamHealComplete: {packet.healerId} healed {packet.patientId}");

            // Apply healing on the patient's client (for multiplayer synchronization)
            Player patient = Utils.GetPlayerById(packet.patientId);
            if (patient != null && patient.IsYourPlayer)
            {
                try
                {
                    // The actual healing was already done on the healer's client,
                    // but we need to ensure the patient's client also applies it
                    // This is a safety measure for multiplayer scenarios
                    Plugin.LogSource.LogDebug($"[Packet] Patient {packet.patientId} received heal complete packet");
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogWarning($"[Packet] TeamHealComplete healing sync error: {ex.Message}");
                }
            }

            try
            {
                string healerDisplay = Utils.GetPlayerDisplayName(packet.healerId);
                string patientDisplay = Utils.GetPlayerDisplayName(packet.patientId);
                VFX_UI.Text(Color.green, $"{patientDisplay} was healed by {healerDisplay}");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[VFX_UI] TeamHealComplete notify failed: {ex.Message}"); }
        }

        private static void OnTeamHealCancelPacketReceived(TeamHealCancelPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, p => SendTeamHealCancelPacket(p.patientId, p.healerId))) return;

            Plugin.LogSource.LogDebug($"[Packet] TeamHealCancel: {packet.healerId} cancelled healing {packet.patientId}");

            try
            {
                string healerDisplay = Utils.GetPlayerDisplayName(packet.healerId);
                VFX_UI.Text(Color.yellow, $"{healerDisplay} cancelled healing");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[VFX_UI] TeamHealCancel notify failed: {ex.Message}"); }
        }

        private static void RelayResyncPacket(PlayerStateResyncPacket packet) => SendPacket(ref packet);

        private static void OnPlayerStateResyncPacketReceived(PlayerStateResyncPacket packet, NetPeer peer)
        {
            if (TryRelayIfHeadless(packet, RelayResyncPacket)) return;

            // We are the authority for our own state — never overwrite ourselves from a resync.
            var local = Utils.GetYourPlayer();
            if (local != null && local.ProfileId == packet.playerId) return;

            var st       = RMSession.GetPlayerState(packet.playerId);
            var incoming = (RMState)packet.state;
            var prev     = st.State;

            // Always update timers — they are time-sensitive values even when state hasn't changed.
            st.CriticalTimer        = packet.criticalTimer;
            st.InvulnerabilityTimer = packet.invulTimer;
            st.CooldownTimer        = packet.cooldownTimer;
            st.CurrentReviverId     = packet.reviverId;

            // If state is already correct there is nothing further to apply.
            if (st.State == incoming) return;

            st.State = incoming;

            Player player = Utils.GetPlayerById(packet.playerId);

            try
            {
                switch (incoming)
                {
                    case RMState.BleedingOut:
                    case RMState.Reviving:
                        RMSession.AddToCriticalPlayers(packet.playerId);
                        st.KillOverride = false;
                        if (player != null)
                        {
                            // Ensure fake items exist for late-joiners who missed the
                            // original BleedingOut/ReviveStart packets and received only
                            // this resync; without them, any in-flight health-sync from
                            // ApplyItem will NullRef in CreateMedsController.
                            MedicalAnimations.EnsureFakeItemsForRemotePlayer(player);
                            if (RevivalModSettings.GHOST_MODE.Value) GhostMode.EnterGhostModeById(packet.playerId);
                            if (RevivalModSettings.GOD_MODE.Value)   GodMode.Enable(player);
                        }
                        break;

                    case RMState.Revived:
                        RMSession.RemovePlayerFromCriticalPlayers(packet.playerId);
                        st.KillOverride = false;
                        if (player != null)
                        {
                            GodMode.ForceEnable(player);
                            GhostMode.ExitGhostMode(player);
                            try { PlayerRestorations.RestoreDestroyedBodyParts(player); } catch { }
                            try
                            {
                                if (player.MovementContext != null)
                                {
                                    player.MovementContext.IsInPronePose = false;
                                    player.MovementContext.SetPoseLevel(1f);
                                    player.MovementContext.EnableSprint(true);
                                }
                                // Re-attach movement hooks unsubscribed when player went down
                                player.MovementContext.OnStateChanged -= player.method_17;
                                player.MovementContext.OnStateChanged += player.method_17;
                                player.MovementContext.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
                                player.MovementContext.PhysicalConditionChanged += player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
                            }
                            catch { }
                        }
                        break;

                    case RMState.CoolDown:
                    case RMState.None:
                        RMSession.RemovePlayerFromCriticalPlayers(packet.playerId);
                        if (player != null)
                        {
                            GhostMode.ExitGhostMode(player);
                            GodMode.Disable(player);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[Resync] Apply-state error for {packet.playerId}: {ex.Message}");
            }

            Plugin.LogSource.LogInfo($"[Resync] {packet.playerId} state {prev} → {incoming}");
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
            managerCreatedEvent.Manager.RegisterPacket<TeamHealPacket, NetPeer>(OnTeamHealPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<TeamHealCompletePacket, NetPeer>(OnTeamHealCompletePacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<TeamHealCancelPacket, NetPeer>(OnTeamHealCancelPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<PlayerStateResyncPacket, NetPeer>(OnPlayerStateResyncPacketReceived);
        }

        public static void InitOnPluginEnabled() =>
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnFikaNetManagerCreated);

        // HealthRestored packet removed: restoration inferred from state transitions
    }
}
