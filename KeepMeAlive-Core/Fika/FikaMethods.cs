//====================[ Imports ]====================
using System;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using KeepMeAlive.Components;        // <-- provides RMSession / RMState
using KeepMeAlive.Features;
using KeepMeAlive.Fika.Packets;
using KeepMeAlive.Helpers;
using UnityEngine;

namespace KeepMeAlive.Fika
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
                bool broadcast = true;
                Singleton<IFikaNetworkManager>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered, broadcast);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError(ex);
            }
        }

        private static void LogStateTransition(string source, string playerId, RMState from, RMState to, string details = "")
        {
            Plugin.LogSource.LogInfo($"[StateTrace:{source}] {playerId}: {from} -> {to} {details}");
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
        public static void SendTeamHealPacket(string patientId, string healerId, string itemId)
        {
            TeamHealPacket packet = new() { patientId = patientId, healerId = healerId, itemId = itemId };
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
                playerId              = playerId,
                state                 = (int)st.State,
                criticalTimer         = st.CriticalTimer,
                invulTimer            = st.InvulnerabilityTimer,
                cooldownTimer         = st.CooldownTimer,
                reviverId             = st.CurrentReviverId ?? "",
                reviveRequestedSource = st.ReviveRequestedSource
            };
            SendPacket(ref packet);
        }

        //====================[ Packet Receivers ]====================
        private static void OnBleedingOutPacketReceived(BleedingOutPacket packet, NetPeer peer)
        {
            // Authority guard: local player already owns this state and should not be
            // overwritten by echoed/delayed network packets.
            var local = Utils.GetYourPlayer();
            if (local != null && local.ProfileId == packet.playerId)
            {
                Plugin.LogSource.LogInfo($"[StateTrace:BleedingOutPacket] {packet.playerId}: ignored local-owner packet (timeRemaining={packet.timeRemaining:F2})");
                return;
            }

            var existing = RMSession.GetPlayerState(packet.playerId);
            // Ignore stale rollback packets once revival has already started/finished.
            if (existing.State is RMState.Reviving or RMState.Revived)
            {
                Plugin.LogSource.LogInfo($"[StateTrace:BleedingOutPacket] {packet.playerId}: ignored stale rollback into BleedingOut (current={existing.State}, timeRemaining={packet.timeRemaining:F2})");
                return;
            }

            var playerState = RMSession.GetPlayerState(packet.playerId);
            var prevState = playerState.State;
            RMSession.SetPlayerState(packet.playerId, RMState.BleedingOut);
            playerState.FinalizedReviveCycleId = -1;
            playerState.CriticalTimer = packet.timeRemaining;
            playerState.IsReviveProgressActive = false;
            playerState.IsBeingRevived = false;
            playerState.IsSelfReviving = false;
            playerState.CurrentReviverId = string.Empty;
            playerState.ReviveRequestedSource = 0;
            
            // Reset kill override to ensure death blocking works for remote players
            playerState.KillOverride = false;
            
            // Ensure player is tracked in critical players list for death blocking
            RMSession.AddToCriticalPlayers(packet.playerId);

            LogStateTransition("BleedingOutPacket", packet.playerId, prevState, playerState.State,
                $"| timer={packet.timeRemaining:F2} reviver='{playerState.CurrentReviverId}' source={playerState.ReviveRequestedSource}");

            Player player = Utils.GetPlayerById(packet.playerId);

            Plugin.LogSource.LogDebug($"[Packet] BleedingOut: {packet.playerId} has {packet.timeRemaining}s left (remote player)");

            // Ghost mode uses profileId; must not gate behind ActiveHealthController check since remote host clients use ObservedHealthController.
            if (KeepMeAliveSettings.GHOST_MODE.Value) GhostMode.EnterGhostModeById(packet.playerId);

            if (player != null)
            {
                if (KeepMeAliveSettings.GOD_MODE.Value) GodMode.Enable(player);
            }
            else
            {
                Plugin.LogSource.LogWarning($"[Packet] BleedingOut: Could not find player for {packet.playerId}");
            }
        }

        private static void OnTeamHelpPacketReceived(TeamHelpPacket packet, NetPeer peer)
        {
            if (!RevivePolicy.IsEnabled(ReviveSource.Team))
            {
                Plugin.LogSource.LogDebug($"[Packet] TeamHelp ignored (team revive disabled): {packet.reviverId} -> {packet.reviveeId}");
                return;
            }

            Plugin.LogSource.LogDebug($"[Packet] TeamHelp: {packet.reviverId} started helping {packet.reviveeId}");

            var playerState = RMSession.GetPlayerState(packet.reviveeId);
            if (playerState.State != RMState.BleedingOut)
            {
                Plugin.LogSource.LogInfo($"[StateTrace:TeamHelpPacket] {packet.reviveeId}: ignored help while state={playerState.State}");
                return;
            }
            playerState.CurrentReviverId = packet.reviverId;
            playerState.IsBeingRevived = true;
            playerState.IsSelfReviving = false;
            // Start watchdog: if the reviver disconnects before ReviveStart arrives, TickDowned will clear IsBeingRevived (#1).
            playerState.BeingRevivedWatchdogTimer = 10f;

            try
            {
                string reviverName = Utils.GetPlayerDisplayName(packet.reviverId);
                string reviveeName = Utils.GetPlayerDisplayName(packet.reviveeId);
                VFX_UI.Text(Color.cyan, $"{reviverName} is helping {reviveeName}");

                // Replace the local revivee's self-revive prompt with a "being revived" indicator
                Player reviveePlayer = Utils.GetPlayerById(packet.reviveeId);
                if (reviveePlayer != null && reviveePlayer.IsYourPlayer)
                {
                    // Cancel self-revive inputs to prevent IsBeingRevived corruption
                    playerState.SelfRevivalKeyHoldDuration.Clear();
                    playerState.SelfReviveHoldTime = 0f;
                    playerState.SelfReviveCommitted = false;
                    playerState.SelfReviveAuthPending = false;
                    playerState.IsSelfReviving = false;

                    VFX_UI.HideObjectivePanel();
                    playerState.RevivePromptTimer?.Stop();
                    playerState.RevivePromptTimer = null;
                    VFX_UI.ObjectivePanel(Color.cyan, VFX_UI.Position.BottomCenter, $"{reviverName} is reviving you...");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[VFX_UI] TeamHelp notify failed: {ex.Message}");
            }
        }

        private static void OnTeamCancelPacketReceived(TeamCancelPacket packet, NetPeer peer)
        {
            Plugin.LogSource.LogDebug($"[Packet] TeamCancel: {packet.reviverId} cancelled helping {packet.reviveeId}");

            var playerState = RMSession.GetPlayerState(packet.reviveeId);
            var prevState = playerState.State;
            // Guard: if revival already started/finished, a late cancel cannot revert it.
            if (playerState.State is RMState.Reviving or RMState.Revived)
            {
                Plugin.LogSource.LogWarning($"[Packet] TeamCancel: {packet.reviverId} cancel ignored — {packet.reviveeId} already in {playerState.State} state");
                return;
            }
            RMSession.SetPlayerState(packet.reviveeId, RMState.BleedingOut);
            playerState.IsBeingRevived = false;
            playerState.IsSelfReviving = false;
            playerState.CurrentReviverId = string.Empty;
            LogStateTransition("TeamCancelPacket", packet.reviveeId, prevState, playerState.State,
                $"| cancelledBy='{packet.reviverId}'");

            // If we are the revivee, restore the self-revive prompt
            Player reviveePlayer = Utils.GetPlayerById(packet.reviveeId);
            if (reviveePlayer != null && reviveePlayer.IsYourPlayer)
            {
                VFX_UI.HideObjectivePanel();
                playerState.RevivePromptTimer?.Stop();
                playerState.RevivePromptTimer = null;

                if (RevivePolicy.IsEnabled(ReviveSource.Self) && (KeepMeAliveSettings.NO_DEFIB_REQUIRED.Value || Utils.HasDefib(reviveePlayer)))
                {
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, $"Revive! [{KeepMeAliveSettings.SELF_REVIVAL_KEY.Value}]");
                }
            }

            try
            {
                string reviverName = Utils.GetPlayerDisplayName(packet.reviverId);
                VFX_UI.Text(Color.yellow, $"{reviverName} cancelled revival");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[VFX_UI] TeamCancel notify failed: {ex.Message}");
            }
        }

        private static void OnSelfReviveStartPacketReceived(SelfReviveStartPacket packet, NetPeer peer)
        {
            bool isLocal = Utils.GetYourPlayer()?.ProfileId == packet.playerId;
            ReviveDebug.Log("Packet_SelfReviveStart_Enter", packet.playerId, isLocal, null);

            Plugin.LogSource.LogDebug($"[Packet] SelfReviveStart: {packet.playerId} started self-revival");

            var playerState = RMSession.GetPlayerState(packet.playerId);
            if (playerState.State == RMState.Revived)
            {
                ReviveDebug.Log("Packet_SelfReviveStart_StaleRevived", packet.playerId, isLocal, null);
                Plugin.LogSource.LogInfo($"[StateTrace:SelfReviveStartPacket] {packet.playerId}: ignored stale start while already Revived");
                return;
            }

            if (playerState.State == RMState.CoolDown || playerState.State == RMState.None)
            {
                ReviveDebug.Log("Packet_SelfReviveStart_InvalidState", packet.playerId, isLocal, $"state={playerState.State}");
                Plugin.LogSource.LogInfo($"[StateTrace:SelfReviveStartPacket] {packet.playerId}: ignored start while state={playerState.State}");
                return;
            }

            if (playerState.State == RMState.Reviving && playerState.IsReviveProgressActive)
            {
                // Do not drop start packets on this latch alone.
                // Re-arming is safer than suppressing because stale progress flags can outlive a previous cycle.
                ReviveDebug.Log("Packet_SelfReviveStart_Duplicate", packet.playerId, isLocal, "rearm");
            }

            var prevState = playerState.State;
            ApplyRevivingState(packet.playerId, playerState, ReviveSource.Self, packet.playerId);
            LogStateTransition("SelfReviveStartPacket", packet.playerId, prevState, playerState.State,
                "| source=Self");

            RMSession.UpdatePlayerState(packet.playerId, playerState);
            ReviveDebug.Log("Packet_SelfReviveStart_StateUpdate", packet.playerId, isLocal, $"prevState={prevState}");

            try
            {
                string display = Utils.GetPlayerDisplayName(packet.playerId);
                VFX_UI.Text(Color.cyan, $"{display} is self-reviving");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[VFX_UI] SelfReviveStart notify failed: {ex.Message}");
            }
        }

        private static void OnTeamReviveStartPacketReceived(TeamReviveStartPacket packet, NetPeer peer)
        {
            if (!RevivePolicy.IsEnabled(ReviveSource.Team))
            {
                Plugin.LogSource.LogDebug($"[Packet] TeamReviveStart ignored (team revive disabled): {packet.reviverId} -> {packet.reviveeId}");
                return;
            }

            Plugin.LogSource.LogDebug($"[Packet] TeamReviveStart: {packet.reviverId} started reviving {packet.reviveeId}");

            var playerState = RMSession.GetPlayerState(packet.reviveeId);
            if (playerState.State == RMState.Revived)
            {
                Plugin.LogSource.LogInfo($"[StateTrace:TeamReviveStartPacket] {packet.reviveeId}: ignored stale start while already Revived");
                return;
            }

            if (playerState.State == RMState.CoolDown || playerState.State == RMState.None)
            {
                Plugin.LogSource.LogInfo($"[StateTrace:TeamReviveStartPacket] {packet.reviveeId}: ignored start while state={playerState.State}");
                return;
            }

            var prevState = playerState.State;
            ApplyRevivingState(packet.reviveeId, playerState, ReviveSource.Team, packet.reviverId);

            LogStateTransition("TeamReviveStartPacket", packet.reviveeId, prevState, playerState.State,
                $"| source=Team reviver='{packet.reviverId}'");

            RMSession.UpdatePlayerState(packet.reviveeId, playerState);

            try
            {
                string reviverName = Utils.GetPlayerDisplayName(packet.reviverId);
                string reviveeName = Utils.GetPlayerDisplayName(packet.reviveeId);
                VFX_UI.Text(Color.cyan, $"{reviverName} is reviving {reviveeName}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[VFX_UI] TeamReviveStart notify failed: {ex.Message}");
            }
        }

        private static void ApplyRevivingState(string playerId, RMPlayer playerState, ReviveSource source, string reviverId)
        {
            RMSession.SetPlayerState(playerId, RMState.Reviving);
            playerState.ReviveRequestedSource = (int)source;
            playerState.CurrentReviverId = source == ReviveSource.Self ? string.Empty : (reviverId ?? string.Empty);
            // Always re-arm local revive progress on new revive transition.
            playerState.IsReviveProgressActive = false;
            playerState.IsBeingRevived = source == ReviveSource.Team;
            playerState.IsSelfReviving = false;
            playerState.BeingRevivedWatchdogTimer = 0f;
            playerState.SelfReviveHoldTime = 0f;
            playerState.SelfReviveCommitted = false;
            playerState.SelfReviveAuthPending = false;
            playerState.SelfRevivalKeyHoldDuration.Clear();
        }

        private static void OnRevivedPacketReceived(RevivedPacket packet, NetPeer peer)
        {
            Plugin.LogSource.LogDebug($"[Packet] Revived: {packet.playerId} was revived by {packet.reviverId}");

            Player player = Utils.GetPlayerById(packet.playerId);
            if (player == null)
            {
                Plugin.LogSource.LogWarning($"[Packet] Revived: Player {packet.playerId} not found");
                return;
            }

            bool isSelfRevive = string.IsNullOrEmpty(packet.reviverId) || packet.reviverId == packet.playerId;
            var playerState = RMSession.GetPlayerState(packet.playerId);
            var prevState = playerState.State;
            
            LogStateTransition("RevivedPacket", packet.playerId, prevState, RMState.Revived,
                $"| reviver='{packet.reviverId}' invul={PostReviveEffects.GetInvulnDuration(isSelfRevive ? ReviveSource.Self : ReviveSource.Team):F2}");

            RevivalController.FinalizeRevivalFromPacket(player, packet.playerId, packet.reviverId);

            try
            {
                string display = Utils.GetPlayerDisplayName(packet.playerId);
                VFX_UI.Text(Color.green, isSelfRevive ? $"{display} self-revived" : $"{display} was revived");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[VFX_UI] Revived notify failed: {ex.Message}");
            }
        }

        private static void OnPlayerStateResetPacketReceived(PlayerStateResetPacket packet, NetPeer peer)
        {
            var playerState = RMSession.GetPlayerState(packet.playerId);
            var prevState = playerState.State;
            RMSession.RemovePlayerFromCriticalPlayers(packet.playerId);
            playerState.IsReviveProgressActive = false;
            playerState.IsBeingRevived = false;
            playerState.CurrentReviverId = string.Empty;
            playerState.ReviveRequestedSource = 0;
            playerState.BeingRevivedWatchdogTimer = 0f;
            playerState.SelfRevivalKeyHoldDuration.Clear();
            playerState.SelfReviveHoldTime = 0f;
            playerState.SelfReviveCommitted = false;
            playerState.SelfReviveAuthPending = false;
            playerState.IsSelfReviving = false;
            playerState.FinalizedReviveCycleId = -1;

            if (packet.isDead)
            {
                Plugin.LogSource.LogDebug($"[Packet] StateReset: {packet.playerId} died");
                RMSession.SetPlayerState(packet.playerId, RMState.None);
                playerState.KillOverride = true;
                playerState.InvulnerabilityTimer = 0f;
                playerState.CriticalTimer = 0f;
            }
            else
            {
                Plugin.LogSource.LogDebug($"[Packet] StateReset: {packet.playerId} entered cooldown ({packet.cooldownSeconds:F0}s)");
                RMSession.SetPlayerState(packet.playerId, RMState.CoolDown);
                playerState.CooldownTimer = packet.cooldownSeconds;
                playerState.KillOverride = false;
                playerState.InvulnerabilityTimer = 0f;
                playerState.CriticalTimer = 0f;
            }

            LogStateTransition("PlayerStateResetPacket", packet.playerId, prevState, playerState.State,
                $"| isDead={packet.isDead} cooldown={packet.cooldownSeconds:F2}");

            Player player = Utils.GetPlayerById(packet.playerId);
            if (player != null)
            {
                if (KeepMeAliveSettings.GHOST_MODE.Value) GhostMode.ExitGhostMode(player);
                GodMode.Disable(player);

                if (packet.isDead)
                {
                    player.ActiveHealthController?.Kill(EDamageType.Undefined);
                }
            }
        }

        //====================[ Team Healing Packet Receivers ]====================
        private static void OnTeamHealPacketReceived(TeamHealPacket packet, NetPeer peer)
        {
            Plugin.LogSource.LogDebug($"[Packet] TeamHeal: {packet.healerId} healing {packet.patientId} with item {packet.itemId}");

            // Show notification on all machines.
            try
            {
                string healerDisplay  = Utils.GetPlayerDisplayName(packet.healerId);
                string patientDisplay = Utils.GetPlayerDisplayName(packet.patientId);
                VFX_UI.Text(Color.green, $"{healerDisplay} is healing {patientDisplay}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[VFX_UI] TeamHeal notify failed: {ex.Message}");
            }

            // Only the patient applies the item — same as UseLooseLoot's ApplyItem call.
            // Fika's existing InventoryPacket broadcast handles draining HpResource on all machines.
            Player patient = Utils.GetPlayerById(packet.patientId);
            if (patient == null || !patient.IsYourPlayer) return;

            try
            {
                Player healer = Utils.GetPlayerById(packet.healerId);
                if (healer?.Inventory == null)
                {
                    Plugin.LogSource.LogWarning("[Packet] TeamHeal: healer inventory not accessible");
                    return;
                }

                if (TeamMedical.FindItemInInventory(healer, packet.itemId) is not Item healerItem)
                {
                    Plugin.LogSource.LogWarning($"[Packet] TeamHeal: item {packet.itemId} not found in healer inventory");
                    return;
                }

                if (!Utils.TryApplyItemLikeTeamHeal(patient, healerItem, "TeamHealPacket"))
                {
                    return;
                }
                VFX_UI.Text(Color.green, "You were healed!");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[Packet] TeamHeal apply error: {ex.Message}");
            }
        }

        private static void OnTeamHealCancelPacketReceived(TeamHealCancelPacket packet, NetPeer peer)
        {
            Plugin.LogSource.LogDebug($"[Packet] TeamHealCancel: {packet.healerId} cancelled healing {packet.patientId}");

            try
            {
                string healerDisplay = Utils.GetPlayerDisplayName(packet.healerId);
                VFX_UI.Text(Color.yellow, $"{healerDisplay} cancelled healing");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[VFX_UI] TeamHealCancel notify failed: {ex.Message}");
            }
        }

        private static void OnPlayerStateResyncPacketReceived(PlayerStateResyncPacket packet, NetPeer peer)
        {
            // Authority guard - never overwrite local from resync
            var local = Utils.GetYourPlayer();
            if (local != null && local.ProfileId == packet.playerId) return;

            var st       = RMSession.GetPlayerState(packet.playerId);
            var incoming = (RMState)packet.state;
            var prev     = st.State;

            // Ignore stale backwards transitions that would interrupt an in-progress revive.
            if (prev is RMState.Reviving or RMState.Revived)
            {
                bool staleRegression = incoming == RMState.BleedingOut || incoming == RMState.Reviving;
                if (staleRegression && incoming != prev)
                {
                    Plugin.LogSource.LogInfo($"[StateTrace:ResyncPacket] {packet.playerId}: ignored stale transition {prev} -> {incoming} | crit={packet.criticalTimer:F2} invul={packet.invulTimer:F2} cd={packet.cooldownTimer:F2} reviver='{packet.reviverId}' source={packet.reviveRequestedSource}");
                    return;
                }
            }

            // Always update timers - time-sensitive even if state unchanged
            st.CriticalTimer         = packet.criticalTimer;
            st.InvulnerabilityTimer  = packet.invulTimer;
            st.CooldownTimer         = packet.cooldownTimer;
            st.CurrentReviverId      = packet.reviverId;
            st.ReviveRequestedSource = packet.reviveRequestedSource;

            if (st.State == incoming) return;

            st.State = incoming;
            Player player = Utils.GetPlayerById(packet.playerId);

            try
            {
                switch (incoming)
                {
                    case RMState.BleedingOut:
                        // fall-through to Reviving logic
                        goto case RMState.Reviving;
                    case RMState.Reviving:
                        RMSession.AddToCriticalPlayers(packet.playerId);
                        st.KillOverride = false;
                        if (player != null)
                        {
                            if (KeepMeAliveSettings.GHOST_MODE.Value) GhostMode.EnterGhostModeById(packet.playerId);
                            if (KeepMeAliveSettings.GOD_MODE.Value)   GodMode.Enable(player);
                        }
                        break;

                    case RMState.Revived:
                        bool applyFinalize = false;
                        if (player != null && player.IsYourPlayer)
                        {
                            applyFinalize = DownedStateController.TryCommitReviveFinalizeForCycle("ResyncRevived", packet.playerId, st);
                        }
                        PostRevivalController.BeginPostRevival(player, packet.playerId, st, applyFinalize);
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

            LogStateTransition("ResyncPacket", packet.playerId, prev, incoming,
                $"| crit={packet.criticalTimer:F2} invul={packet.invulTimer:F2} cd={packet.cooldownTimer:F2} reviver='{packet.reviverId}' source={packet.reviveRequestedSource}");
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
            managerCreatedEvent.Manager.RegisterPacket<TeamHealCancelPacket, NetPeer>(OnTeamHealCancelPacketReceived);
            managerCreatedEvent.Manager.RegisterPacket<PlayerStateResyncPacket, NetPeer>(OnPlayerStateResyncPacketReceived);
        }

        public static void InitOnPluginEnabled() =>
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnFikaNetManagerCreated);

        // HealthRestored packet removed: restoration inferred from state transitions
    }
}