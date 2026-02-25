using System;
using System.Collections;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.UI;
using UnityEngine;
using RevivalMod.Helpers;
using RevivalMod.Components;
using RevivalMod.Fika;
using RevivalMod.Patches;

namespace RevivalMod.Features
{
    internal static class DownedStateController
    {
        private enum ReviveSource { Self = 0, Team = 1 }

        private static IEnumerator DelayedActionAfterSeconds(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            try { action?.Invoke(); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] delayed action error: {ex.Message}"); }
        }

        private static void ClearTimers(RMPlayer st)
        {
            st.CriticalStateMainTimer?.Stop(); st.CriticalStateMainTimer = null;
            st.RevivePromptTimer?.Stop(); st.RevivePromptTimer = null;
        }

        private static RMPlayer GetState(Player p) => RMSession.GetPlayerState(p.ProfileId);

        private static void HideAllPanelsAndStop(RMPlayer st)
        {
            VFX_UI.HideTransitPanel();
            VFX_UI.HideObjectivePanel();
            ClearTimers(st);
        }

        // ── Collider State ──────────────────────────────────────────────

        public static void TickBodyInteractableColliderState(Player player)
        {
            try
            {
                if (player?.HealthController == null) return;

                bool isCritical = RMSession.IsPlayerCritical(player.ProfileId);
                bool isInjured = false;

                if (!isCritical)
                {
                    var parts = new[]
                    {
                        EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach,
                        EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg
                    };
                    foreach (var part in parts)
                    {
                        var hp = player.HealthController.GetBodyPartHealth(part);
                        if (hp.Current < hp.Maximum) { isInjured = true; break; }
                    }
                }

                bool shouldEnable = isCritical || isInjured;

                foreach (var bi in player.GetComponentsInChildren<BodyInteractable>(true))
                {
                    if (bi.Revivee != null && bi.Revivee.ProfileId == player.ProfileId && bi.TryGetComponent<BoxCollider>(out var col))
                    {
                        if (col.enabled != shouldEnable) col.enabled = shouldEnable;
                        break;
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] TickCollider error: {ex.Message}"); }
        }

        // ── Enter / Exit Downed ─────────────────────────────────────────

        public static void EnterDowned(Player player, EDamageType damageType)
        {
            if (player is null) return;

            try
            {
                string id = player.ProfileId;
                var st = RMSession.GetPlayerState(id);

                if (st.State is RMState.BleedingOut or RMState.Reviving or RMState.Revived) return;
                if (st.State == RMState.CoolDown)
                {
                    DeathMode.ForceBleedout(player);
                    return;
                }

                st.CooldownTimer = 0f;
                st.KillOverride = false;
                st.PlayerDamageType = damageType;
                st.State = RMState.BleedingOut;
                st.CriticalTimer = RevivalModSettings.CRITICAL_STATE_TIME.Value;

                MedicalAnimations.CreateInQuestInventory(player, MedicalAnimations.SurgicalItemType.CMS);
                MedicalAnimations.CreateInQuestInventory(player, MedicalAnimations.SurgicalItemType.SurvKit);

                st.RevivalRequested = false;
                st.ReviveRequestedSource = 0;

                ApplyCriticalEffects(player);
                ApplyRevivableState(player);

                RMSession.AddToCriticalPlayers(id);
                ShowCriticalStateUI(player, st);
                FikaBridge.SendBleedingOutPacket(id, st.CriticalTimer);
                RevivalAuthority.NotifyBeginCritical(id);
                st.ResyncCooldown = -1f; // immediate resync on state entry

                if (RevivalModSettings.GHOST_MODE.Value) GhostMode.EnterGhostModeById(id);
                if (RevivalModSettings.GOD_MODE.Value) GodMode.Enable(player);

                Plugin.LogSource.LogInfo($"[Downed] Player {id} entered critical state");
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] EnterDowned error: {ex.Message}"); }
        }

        public static void ExitDowned(Player player)
        {
            if (player is null) return;

            var st = GetState(player);
            st.State = RMState.None;
            HideAllPanelsAndStop(st);

            if (st.InvulnerabilityTimer <= 0)
            {
                RemoveRevivableState(player);
                PlayerRestorations.RestorePlayerMovement(player);
            }

            try { MedicalAnimations.CleanupAllFakeItems(player); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ExitDowned CleanupFakeItems error: {ex.Message}"); }

            GodMode.Disable(player);
        }

        // ── Per-Frame Ticks ─────────────────────────────────────────────

        public static void TickDowned(Player player)
        {
            var st = GetState(player);
            if (!st.IsCritical) return;

            PlayerRestorations.StoreOriginalMovementSpeed(player);
            ApplyDownedMovementSpeed(player, st);
            ApplyDownedMovementRestrictions(player, st);

            st.CriticalStateMainTimer?.Update();
            st.RevivePromptTimer?.Update();

            if (st.CriticalStateMainTimer is { IsRunning: true })
            {
                // Accurate DateTime-based countdown drives the authoritative timer.
                TimeSpan remain = st.CriticalStateMainTimer.GetTimeSpan();
                st.CriticalTimer = (float)remain.TotalSeconds;
            }
            else if (st.State == RMState.BleedingOut)
            {
                // UI panel wasn't available yet (common on Fika clients before first render).
                // Count down directly so bleedout still triggers, and retry showing the panel.
                st.CriticalTimer -= Time.deltaTime;
                if (st.CriticalStateMainTimer == null)
                    TryLazyShowTransitTimer(player, st);
            }

            HandleSelfRevival_RequestSession(player, st);
            ObserveRevivingState(player, st);

            if (st.State == RMState.BleedingOut && (st.CriticalTimer <= 0f || Input.GetKeyDown(RevivalModSettings.GIVE_UP_KEY.Value)))
            {
                ClearTimers(st);
                DeathMode.ForceBleedout(player);
            }
        }

        public static void TickInvulnerability(Player player)
        {
            var st = GetState(player);
            if (st.State != RMState.Revived) return;

            st.RevivePromptTimer?.Update();

            float t = st.InvulnerabilityTimer;
            if (!(t > 0f)) return;
            t -= Time.deltaTime;
            st.InvulnerabilityTimer = t;

            if (t <= 0f) EndInvulnerability(player);
        }

        public static void TickCooldown(Player player)
        {
            var st = GetState(player);
            if (st.State != RMState.CoolDown) return;

            float t = st.CooldownTimer;
            if (!(t > 0f)) return;

            st.CooldownTimer = t - Time.deltaTime;

            if (st.CooldownTimer <= 0f)
            {
                st.State = RMState.None;
                st.CooldownTimer = 0f;
                if (player.IsYourPlayer) VFX_UI.Text(Color.green, "Revival cooldown ended - you can now be revived");
            }
        }

        public static void ForceBleedout(Player player) => DeathMode.ForceBleedout(player);

        /// <summary>
        /// Periodically broadcasts our own state to all peers so late-joiners and
        /// packet-loss victims can recover accurate state without a full reconnect.
        /// Call once per frame for the local player only.
        /// </summary>
        public static void TickResync(Player player)
        {
            if (!player.IsYourPlayer) return;

            var st = GetState(player);
            if (st.State == RMState.None) return;   // nothing interesting to broadcast

            st.ResyncCooldown -= Time.deltaTime;
            if (st.ResyncCooldown > 0f) return;

            st.ResyncCooldown = 5f;                 // rebroadcast every 5 seconds
            FikaBridge.SendPlayerStateResyncPacket(player.ProfileId, st);
        }

        // ── UI ──────────────────────────────────────────────────────────

        private static void ShowCriticalStateUI(Player player, RMPlayer st)
        {
            try
            {
                if (!player.IsYourPlayer) return;

                VFX_UI.Text(Color.red, "DOWNED");

                st.CriticalStateMainTimer = VFX_UI.TransitPanel(
                    VFX_UI.Gradient(Color.red, Color.black),
                    VFX_UI.Position.MiddleCenter,
                    "BLEEDING OUT",
                    RevivalModSettings.CRITICAL_STATE_TIME.Value
                );

                if (RevivalModSettings.SELF_REVIVAL_ENABLED.Value && Utils.HasDefib(player))
                {
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter,
                        $"Revive! [{RevivalModSettings.SELF_REVIVAL_KEY.Value}]");
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ShowCriticalStateUI error: {ex.Message}"); }
        }

        /// <summary>
        /// Called every tick while CriticalStateMainTimer is null during BleedingOut.
        /// Retries creating the transit panel timer with the remaining time, so it picks
        /// up as soon as LocationTransitTimerPanel becomes available (fixes Fika client
        /// first-death where the panel is not yet initialized on the opening frame).
        /// Does NOT re-show the "DOWNED" notification or the self-revive objective.
        /// </summary>
        private static void TryLazyShowTransitTimer(Player player, RMPlayer st)
        {
            if (!player.IsYourPlayer) return;
            st.CriticalStateMainTimer = VFX_UI.TransitPanel(
                VFX_UI.Gradient(Color.red, Color.black),
                VFX_UI.Position.MiddleCenter,
                "BLEEDING OUT",
                Math.Max(0.1f, st.CriticalTimer)
            );
        }

        // ── Critical Effects ────────────────────────────────────────────

        private static void ApplyCriticalEffects(Player player)
        {
            try
            {
                var st = GetState(player);
                PlayerRestorations.StoreOriginalMovementSpeed(player);

                if (player?.ActiveHealthController != null)
                {
                    if (RevivalModSettings.CONTUSION_EFFECT.Value)
                        player.ActiveHealthController.DoContusion(RevivalModSettings.CRITICAL_STATE_TIME.Value, 1f);
                    if (RevivalModSettings.STUN_EFFECT.Value)
                        player.ActiveHealthController.DoStun(Math.Min(RevivalModSettings.CRITICAL_STATE_TIME.Value, 20f), 1f);
                }

                ApplyDownedMovementSpeed(player, st);
                try { player.MovementContext?.SetPoseLevel(0f, true); } catch { }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ApplyCriticalEffects error: {ex.Message}"); }
        }

        private static IEnumerator DeferredSetEmptyHands(Player player)
        {
            yield return null; // wait one frame so pending animation events (e.g. OnAddAmmoInChamber) fire normally
            try { if (player != null) player.SetEmptyHands(null); }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[DownedStateController] DeferredSetEmptyHands warn: {ex.Message}"); }
        }

        private static void ApplyRevivableState(Player player)
        {
            try
            {
                PlayerRestorations.SetAwarenessZero(player);

                // Defer by one frame: SetEmptyHands triggers a hands-controller transition.
                // Calling it mid-tick (while Kill() fires inside UpdateTick) leaves pending
                // animation events (like OnAddAmmoInChamber) to fire against a torn-down
                // FirearmController, causing a NullRef in PoolManagerClass.CreateItem.
                Plugin.StaticCoroutineRunner.StartCoroutine(DeferredSetEmptyHands(player));

                player.MovementContext.EnableSprint(false);
                player.MovementContext.SetPoseLevel(0f, true);
                player.MovementContext.IsInPronePose = true;

                if (player.ShouldVocalizeDeath(player.LastDamagedBodyPart))
                {
                    var trig = player.LastDamageType.IsWeaponInduced() ? EPhraseTrigger.OnDeath : EPhraseTrigger.OnAgony;
                    try { player.Speaker.Play(trig, player.HealthStatus, true, null); } catch { }
                }

                player.MovementContext.ReleaseDoorIfInteractingWithOne();
                player.MovementContext.OnStateChanged -= player.method_17;
                player.MovementContext.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;

                player.MovementContext.StationaryWeapon?.Unlock(player.ProfileId);
                if (player.MovementContext.StationaryWeapon != null &&
                    player.MovementContext.StationaryWeapon.Item == player.HandsController.Item)
                {
                    player.MovementContext.StationaryWeapon.Show();
                    player.ReleaseHand();
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ApplyRevivableState error: {ex.Message}"); }
        }

        // ── Self Revival ────────────────────────────────────────────────

        private static void HandleSelfRevival_RequestSession(Player player, RMPlayer st)
        {
            if (!RevivalModSettings.SELF_REVIVAL_ENABLED.Value) return;

            // Block self-revive input while a teammate is actively reviving us.
            // CurrentReviverId is set by OnTeamHelpPacketReceived and cleared by
            // OnTeamCancelPacketReceived, so this correctly gates the entire hold.
            if (!string.IsNullOrEmpty(st.CurrentReviverId) && st.CurrentReviverId != player.ProfileId)
                return;

            KeyCode key = RevivalModSettings.SELF_REVIVAL_KEY.Value;

            if (Input.GetKeyDown(key))
            {
                if (!Utils.HasDefib(player))
                {
                    VFX_UI.Text(Color.red, "No defibrillator found! Unable to revive!");
                    return;
                }

                st.SelfRevivalKeyHoldDuration[key] = 0f;
                st.IsBeingRevived = true;

                VFX_UI.HideObjectivePanel();
                st.RevivePromptTimer = VFX_UI.ObjectivePanel(
                    VFX_UI.Gradient(Color.blue, Color.green),
                    VFX_UI.Position.BottomCenter, "Hold {0:F1}", 2f);
                VFX_UI.EnsureTransitPanelPosition();
            }

            if (Input.GetKey(key) && st.SelfRevivalKeyHoldDuration.ContainsKey(key))
            {
                st.SelfRevivalKeyHoldDuration[key] += Time.deltaTime;
                if (st.SelfRevivalKeyHoldDuration[key] >= 2f)
                {
                    st.SelfRevivalKeyHoldDuration.Remove(key);
                    st.ReviveRequestedSource = (int)ReviveSource.Self;

                    if (RevivalAuthority.TryAuthorizeReviveStart(player.ProfileId, player.ProfileId, "self", out var denyReason))
                    {
                        if (!RevivalModSettings.TESTING.Value)
                            Utils.ConsumeDefibItem(player, Utils.GetDefib(player));

                        st.State = RMState.Reviving;
                        RMSession.UpdatePlayerState(player.ProfileId, st);
                        FikaBridge.SendSelfReviveStartPacket(player.ProfileId);
                    }
                    else
                    {
                        VFX_UI.Text(Color.yellow, string.IsNullOrEmpty(denyReason) ? "Revive denied by server" : denyReason);
                    }
                }
            }

            if (Input.GetKeyUp(key) && st.SelfRevivalKeyHoldDuration.ContainsKey(key))
            {
                st.IsBeingRevived = false;
                st.IsPlayingRevivalAnimation = false;

                VFX_UI.HideObjectivePanel();
                st.RevivePromptTimer?.Stop(); st.RevivePromptTimer = null;

                if (RevivalModSettings.SELF_REVIVAL_ENABLED.Value && Utils.HasDefib(player))
                {
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter,
                        $"Revive! [{RevivalModSettings.SELF_REVIVAL_KEY.Value}]");
                }

                VFX_UI.Text(Color.yellow, "Self-revive canceled");
                st.SelfRevivalKeyHoldDuration.Remove(key);
            }
        }

        private static void ObserveRevivingState(Player player, RMPlayer st)
        {
            if (st.State != RMState.Reviving || st.IsPlayingRevivalAnimation) return;

            st.IsPlayingRevivalAnimation = true;
            var source = (ReviveSource)st.ReviveRequestedSource;

            float revivalDuration = source == ReviveSource.Team
                ? RevivalModSettings.TEAMMATE_REVIVE_ANIMATION_DURATION.Value
                : RevivalModSettings.SELF_REVIVE_ANIMATION_DURATION.Value;

            st.CriticalStateMainTimer?.Stop();
            st.CriticalStateMainTimer = VFX_UI.TransitPanel(
                VFX_UI.Gradient(Color.red, Color.green),
                VFX_UI.Position.MiddleCenter, "REVIVING", revivalDuration);

            VFX_UI.HideObjectivePanel();
            st.RevivePromptTimer?.Stop(); st.RevivePromptTimer = null;

            if (source == ReviveSource.Team && player.IsYourPlayer)
            {
                st.RevivePromptTimer = VFX_UI.ObjectivePanel(
                    VFX_UI.Gradient(Color.blue, Color.green),
                    VFX_UI.Position.BottomCenter, "Being Revived {0:F1}", revivalDuration);
            }

            // Only play a meds-item animation (which calls ApplyItem and generates Fika
            // HealthSyncPackets referencing our per-player fake item IDs) for SELF-revivals,
            // where the local player animates their own hands.
            //
            // For TEAM revivals we intentionally skip UseWithDuration: the fake item
            // (generated per-player via dea1/dea2 prefix + ProfileId suffix) lives in
            // QuestRaidItems but is NOT registered in Fika's per-player network item cache
            // built at session start.  Any HealthSyncPacket the host receives that references
            // these IDs will cause "Could not find item" → NullRef spam in
            // NetworkHealthControllerAbstractClass.  The revival completion is driven purely
            // by the DelayedActionAfterSeconds coroutine below, so the animation call is not
            // needed for correctness.
            if (source == ReviveSource.Self)
            {
                _ = MedicalAnimations.UseWithDuration(player, MedicalAnimations.SurgicalItemType.SurvKit, revivalDuration);
            }

            Plugin.StaticCoroutineRunner.StartCoroutine(
                DelayedActionAfterSeconds(revivalDuration, () => OnRevivalAnimationComplete(player)));
        }

        // ── Revival Completion ──────────────────────────────────────────

        private static void OnRevivalAnimationComplete(Player player)
        {
            if (player is null) return;
            var st = GetState(player);
            if (st.State != RMState.Reviving) return;

            var src = (ReviveSource)st.ReviveRequestedSource;

            if (src == ReviveSource.Self)
            {
                CompleteRevival(player, "", "Defibrillator used successfully! You are temporarily invulnerable but limited in movement.");
            }
            else
            {
                CompleteRevival(player, st.CurrentReviverId ?? "", "Revived by teammate! You are temporarily invulnerable.");
            }
        }

        public static bool StartTeammateRevive(string reviveeId, string reviverId = "")
        {
            try
            {
                if (!RMSession.IsPlayerCritical(reviveeId)) return false;

                var st = RMSession.GetPlayerState(reviveeId);
                if (!RevivalAuthority.TryAuthorizeReviveStart(reviveeId, reviverId, "team", out var denyReason))
                {
                    Plugin.LogSource.LogWarning($"[Downed] teammate revive denied for {reviveeId}: {denyReason}");
                    return false;
                }

                st.State = RMState.Reviving;
                st.ReviveRequestedSource = (int)ReviveSource.Team;
                st.CurrentReviverId = reviverId;
                RMSession.UpdatePlayerState(reviveeId, st);
                return true;
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] StartTeammateRevive error: {ex}"); return false; }
        }

        public static void CompleteTeammateRevival(Player player)
        {
            if (player is null) return;
            var st = GetState(player);
            CompleteRevival(player, st.CurrentReviverId ?? "", "Revived by teammate! You are temporarily invulnerable.");
        }

        private static void CompleteRevival(Player player, string reviverId, string message)
        {
            var st = GetState(player);
            st.State = RMState.Revived;
            RMSession.UpdatePlayerState(player.ProfileId, st);

            GodMode.ForceEnable(player);

            RevivalAuthority.NotifyReviveComplete(player.ProfileId, reviverId);
            FikaBridge.SendRevivedPacket(player.ProfileId, reviverId);
            var __st = GetState(player); __st.ResyncCooldown = -1f; // immediate resync
            FinishRevive(player, player.ProfileId, message);
        }

        // ── Post-Revival ────────────────────────────────────────────────

        private static void FinishRevive(Player player, string playerId, string msg)
        {
            var st = RMSession.GetPlayerState(playerId);

            try { GhostMode.ExitGhostMode(player); } catch { }

            try { PlayerRestorations.RestoreDestroyedBodyParts(player); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] RestoreBodyHealth error: {ex.Message}"); }

            StartInvulnerabilityPeriod(player, st);

            st.IsPlayingRevivalAnimation = false;
            st.IsBeingRevived = false;
            st.KillOverride = false;
            st.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ClearTimers(st);

            RMSession.RemovePlayerFromCriticalPlayers(playerId);

            try { MedicalAnimations.CleanupAllFakeItems(player); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] CleanupFakeItems error: {ex.Message}"); }

            VFX_UI.Text(Color.green, msg);
            Plugin.LogSource.LogInfo($"[Downed] revive complete for {playerId}");
        }

        private static void StartInvulnerabilityPeriod(Player player, RMPlayer st)
        {
            try
            {
                st.InvulnerabilityTimer = RevivalModSettings.REVIVAL_DURATION.Value;

                if (st.OriginalMovementSpeed > 0) player.Physical.WalkSpeedLimit = st.OriginalMovementSpeed;

                if (player.MovementContext != null)
                {
                    player.MovementContext.IsInPronePose = false;
                    player.MovementContext.SetPoseLevel(1f);
                    player.MovementContext.EnableSprint(true);
                }

                // Re-attach event hooks that were removed in ApplyRevivableState so that
                // EFT's movement state machine and weapon animation system work again.
                try
                {
                    player.MovementContext.OnStateChanged -= player.method_17;
                    player.MovementContext.OnStateChanged += player.method_17;
                    player.MovementContext.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
                    player.MovementContext.PhysicalConditionChanged += player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
                }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"[DownedStateController] Re-hook movement events error: {ex.Message}"); }

                if (player.IsYourPlayer)
                {
                    VFX_UI.HideTransitPanel();
                    st.CriticalStateMainTimer?.Stop(); st.CriticalStateMainTimer = null;
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter,
                        "Invulnerable {0:F1}", RevivalModSettings.REVIVAL_DURATION.Value);
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] StartInvulnerabilityPeriod error: {ex.Message}"); }
        }

        private static void EndInvulnerability(Player player)
        {
            var st = GetState(player);
            st.CurrentReviverId = "";

            if (RevivalModSettings.GHOST_MODE.Value) GhostMode.ExitGhostMode(player);
            GodMode.Disable(player);

            RemoveRevivableState(player);
            PlayerRestorations.RestorePlayerMovement(player);

            st.State = RMState.CoolDown;
            st.CooldownTimer = RevivalModSettings.REVIVAL_COOLDOWN.Value;
            st.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (player.IsYourPlayer)
            {
                RevivalAuthority.NotifyEndInvulnerability(player.ProfileId, RevivalModSettings.REVIVAL_COOLDOWN.Value);
                FikaBridge.SendPlayerStateResetPacket(player.ProfileId, isDead: false, RevivalModSettings.REVIVAL_COOLDOWN.Value);
                GetState(player).ResyncCooldown = -1f; // immediate resync
                VFX_UI.HideObjectivePanel();
                VFX_UI.Text(Color.cyan, $"Invulnerability ended. Revival cooldown: {RevivalModSettings.REVIVAL_COOLDOWN.Value:F0}s");
            }
        }

        private static void RemoveRevivableState(Player player)
        {
            try
            {
                var st = GetState(player);
                if (st.HasStoredAwareness)
                {
                    PlayerRestorations.RestoreAwareness(player);
                    if (RevivalModSettings.GHOST_MODE.Value) GhostMode.ExitGhostMode(player);
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] RemoveRevivableState error: {ex.Message}"); }
        }

        // ── Movement Helpers ────────────────────────────────────────────

        private static void ApplyDownedMovementSpeed(Player player, RMPlayer st)
        {
            try
            {
                bool frozen = st.State == RMState.Reviving || st.SelfRevivalKeyHoldDuration.Count > 0;
                float baseSpd = st.OriginalMovementSpeed > 0 ? st.OriginalMovementSpeed : player.Physical.WalkSpeedLimit;
                player.Physical.WalkSpeedLimit = frozen
                    ? 0f
                    : Mathf.Max(0.1f, baseSpd * (RevivalModSettings.DOWNED_MOVEMENT_SPEED.Value / 100f));
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ApplyDownedMovementSpeed error: {ex.Message}"); }
        }

        private static void ApplyDownedMovementRestrictions(Player player, RMPlayer st)
        {
            try
            {
                if (player.MovementContext == null) return;

                player.MovementContext.SetPoseLevel(0f, true);
                player.MovementContext.IsInPronePose = true;
                player.ActiveHealthController.SetStaminaCoeff(1f);

                if (st.State != RMState.Reviving) player.SetEmptyHands(null);
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ApplyDownedMovementRestrictions error: {ex.Message}"); }
        }

        // ── Queries ─────────────────────────────────────────────────────

        public static bool IsRevivalOnCooldown(string playerId) => RMSession.GetPlayerState(playerId).State == RMState.CoolDown;
        public static bool IsPlayerInCriticalState(string playerId) => RMSession.HasPlayerState(playerId) && RMSession.GetPlayerState(playerId).IsCritical;
        public static bool IsPlayerInvulnerable(string playerId) => RMSession.HasPlayerState(playerId) && RMSession.GetPlayerState(playerId).IsInvulnerable;

        public static void SetPlayerCriticalState(Player player, bool critical, EDamageType damageType)
        {
            if (player is null) return;
            if (critical) EnterDowned(player, damageType);
            else ExitDowned(player);
        }
    }
}
