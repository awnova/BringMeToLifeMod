//====================[ Imports ]====================
using System;
using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.UI;
using UnityEngine;
using KeepMeAlive.Helpers;
using KeepMeAlive.Components;
using KeepMeAlive.Fika;
using KeepMeAlive.Patches;

namespace KeepMeAlive.Features
{
    //====================[ DownedStateController ]====================
    internal static class DownedStateController
    {
        //====================[ Enums & Fields ]====================
        private enum ReviveSource { Self = 0, Team = 1 }

        // OPTIMIZATION: Cache BodyInteractable refs to avoid GetComponentsInChildren every tick.
        // Storing BodyInteractable (not just BoxCollider) lets us read HasActivePicker.
        private static readonly Dictionary<string, BodyInteractable> _bodyInteractableCache = new Dictionary<string, BodyInteractable>();

        // OPTIMIZATION: Static array prevents allocating memory on every tick/method call
        private static readonly EBodyPart[] TrackedBodyParts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach,
            EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg
        };

        //====================[ Helper Methods ]====================
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

        //====================[ Collider State ]====================
        public static void TickBodyInteractableColliderState(Player player)
        {
            if (player?.HealthController == null || player.IsAI) return;

            try
            {
                bool isCritical = RMSession.IsPlayerCritical(player.ProfileId);
                bool isInjured = false;

                if (!isCritical)
                {
                    for (int i = 0; i < TrackedBodyParts.Length; i++)
                    {
                        var hp = player.HealthController.GetBodyPartHealth(TrackedBodyParts[i]);
                        if (hp.Current >= hp.Maximum) continue;
                        isInjured = true;
                        break;
                    }
                }

                bool shouldEnable = isCritical || isInjured;

                if (!_bodyInteractableCache.TryGetValue(player.ProfileId, out var bi) || bi == null)
                {
                    foreach (var found in player.GetComponentsInChildren<BodyInteractable>(true))
                    {
                        if (found.Revivee?.ProfileId == player.ProfileId)
                        {
                            _bodyInteractableCache[player.ProfileId] = bi = found;
                            break;
                        }
                    }
                }

                if (bi != null && bi.TryGetComponent(out BoxCollider col))
                {
                    // Don't fight the picker: if a MedPickerInteractable is open, keep this collider off.
                    bool canEnable = shouldEnable && !bi.HasActivePicker;
                    if (col.enabled != canEnable) col.enabled = canEnable;
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] TickCollider error: {ex.Message}"); }
        }

        //====================[ Enter / Exit Downed ]====================
        public static void EnterDowned(Player player, EDamageType damageType)
        {
            if (player == null) return;

            try
            {
                string id = player.ProfileId;
                var st = RMSession.GetPlayerState(id);

                if (st.State is RMState.BleedingOut or RMState.Reviving or RMState.Revived) return;
                
                if (st.State == RMState.CoolDown)
                {
                    _bodyInteractableCache.Remove(player.ProfileId);
                    DeathMode.ForceBleedout(player);
                    return;
                }

                st.CooldownTimer = 0f;
                st.KillOverride = false;
                st.PlayerDamageType = damageType;
                st.State = RMState.BleedingOut;
                st.CriticalTimer = RevivalModSettings.CRITICAL_STATE_TIME.Value;
                st.RevivalRequested = false;
                st.ReviveRequestedSource = 0;

                RMSession.AddToCriticalPlayers(id);
                RestoreVitalsToMinimum(player);

                if (RevivalModSettings.GHOST_MODE.Value) GhostMode.EnterGhostModeById(id);
                if (RevivalModSettings.GOD_MODE.Value) GodMode.Enable(player);

                if (player.IsYourPlayer)
                {
                    FikaBridge.SendBleedingOutPacket(id, st.CriticalTimer);
                    RevivalAuthority.NotifyBeginCritical(id);
                    st.ResyncCooldown = -1f;

                    // Formatted onto single lines to reduce vertical bloat while preserving independent failure safety
                    try { MedicalAnimations.CreateInQuestInventory(player, MedicalAnimations.SurgicalItemType.CMS); } catch (Exception ex) { Plugin.LogSource.LogError($"CMS error: {ex.Message}"); }
                    try { MedicalAnimations.CreateInQuestInventory(player, MedicalAnimations.SurgicalItemType.SurvKit); } catch (Exception ex) { Plugin.LogSource.LogError($"SurvKit error: {ex.Message}"); }

                    ApplyCriticalEffects(player);
                    ApplyRevivableState(player);
                    ShowCriticalStateUI(player, st);
                }

                Plugin.LogSource.LogInfo($"[Downed] Player {id} entered critical state (local={player.IsYourPlayer})");
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] EnterDowned error: {ex.Message}"); }
        }

        public static void ExitDowned(Player player)
        {
            if (player == null) return;

            var st = GetState(player);
            st.State = RMState.None;
            HideAllPanelsAndStop(st);

            if (st.InvulnerabilityTimer <= 0)
            {
                RemoveRevivableState(player);
                PlayerRestorations.RestorePlayerMovement(player);
                st.OriginalMovementSpeed = -1f;
            }

            try { MedicalAnimations.CleanupAllFakeItems(player); } catch (Exception ex) { Plugin.LogSource.LogError($"CleanupFakeItems error: {ex.Message}"); }

            GodMode.Disable(player);
            _bodyInteractableCache.Remove(player.ProfileId);
        }

        //====================[ Per-Frame Ticks ]====================
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
                st.CriticalTimer = (float)st.CriticalStateMainTimer.GetTimeSpan().TotalSeconds;
            }
            else if (st.State == RMState.BleedingOut)
            {
                st.CriticalTimer -= Time.deltaTime;
                if (st.CriticalStateMainTimer == null) TryLazyShowTransitTimer(player, st);
            }

            HandleSelfRevival_RequestSession(player, st);
            ObserveRevivingState(player, st);

            if (st.State == RMState.BleedingOut && (st.CriticalTimer <= 0f || Input.GetKeyDown(RevivalModSettings.GIVE_UP_KEY.Value)))
            {
                ClearTimers(st);
                _bodyInteractableCache.Remove(player.ProfileId);
                DeathMode.ForceBleedout(player);
            }
        }

        public static void TickInvulnerability(Player player)
        {
            var st = GetState(player);
            if (st.State != RMState.Revived || st.InvulnerabilityTimer <= 0f) return;

            st.RevivePromptTimer?.Update();
            st.InvulnerabilityTimer -= Time.deltaTime;

            if (st.InvulnerabilityTimer <= 0f) EndInvulnerability(player);
        }

        public static void TickCooldown(Player player)
        {
            var st = GetState(player);
            if (st.State != RMState.CoolDown || st.CooldownTimer <= 0f) return;

            st.CooldownTimer -= Time.deltaTime;

            if (st.CooldownTimer <= 0f)
            {
                st.State = RMState.None;
                st.CooldownTimer = 0f;
                if (player.IsYourPlayer) VFX_UI.Text(Color.green, "Revival cooldown ended - you can now be revived");
            }
        }

        public static void ForceBleedout(Player player)
        {
            if (player != null) _bodyInteractableCache.Remove(player.ProfileId);
            DeathMode.ForceBleedout(player);
        }

        public static void TickResync(Player player)
        {
            if (!player.IsYourPlayer) return;

            var st = GetState(player);
            if (st.State == RMState.None || (st.ResyncCooldown -= Time.deltaTime) > 0f) return;

            st.ResyncCooldown = 5f; 
            FikaBridge.SendPlayerStateResyncPacket(player.ProfileId, st);
        }

        //====================[ UI ]====================
        private static void ShowCriticalStateUI(Player player, RMPlayer st)
        {
            if (!player.IsYourPlayer) return;
            try
            {
                VFX_UI.Text(Color.red, "DOWNED");
                st.CriticalStateMainTimer = VFX_UI.TransitPanel(VFX_UI.Gradient(Color.red, Color.black), VFX_UI.Position.MiddleCenter, "BLEEDING OUT", RevivalModSettings.CRITICAL_STATE_TIME.Value);

                if (RevivalModSettings.SELF_REVIVAL_ENABLED.Value && Utils.HasDefib(player))
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, $"Revive! [{RevivalModSettings.SELF_REVIVAL_KEY.Value}]");
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ShowCriticalStateUI error: {ex.Message}"); }
        }

        private static void TryLazyShowTransitTimer(Player player, RMPlayer st)
        {
            if (!player.IsYourPlayer) return;
            st.CriticalStateMainTimer = VFX_UI.TransitPanel(VFX_UI.Gradient(Color.red, Color.black), VFX_UI.Position.MiddleCenter, "BLEEDING OUT", Math.Max(0.1f, st.CriticalTimer));
        }

        //====================[ Critical Effects ]====================
        private static void RestoreVitalsToMinimum(Player player)
        {
            if (player?.ActiveHealthController is not { } hc) return;
            try
            {
                for (int i = 0; i < TrackedBodyParts.Length; i++)
                {
                    var part = TrackedBodyParts[i];
                    if (hc.IsBodyPartDestroyed(part) && hc.FullRestoreBodyPart(part))
                    {
                        float delta = 1f - hc.GetBodyPartHealth(part).Current;
                        if (delta < -0.01f) hc.ChangeHealth(part, delta, default);
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] RestoreVitalsToMinimum error: {ex.Message}"); }
        }

        private static void ApplyCriticalEffects(Player player)
        {
            try
            {
                var st = GetState(player);
                PlayerRestorations.StoreOriginalMovementSpeed(player);

                if (player?.ActiveHealthController != null)
                {
                    if (RevivalModSettings.CONTUSION_EFFECT.Value) player.ActiveHealthController.DoContusion(RevivalModSettings.CRITICAL_STATE_TIME.Value, 1f);
                    if (RevivalModSettings.STUN_EFFECT.Value) player.ActiveHealthController.DoStun(Math.Min(RevivalModSettings.CRITICAL_STATE_TIME.Value, 20f), 1f);
                }

                ApplyDownedMovementSpeed(player, st);
                try { player.MovementContext?.SetPoseLevel(0f, true); } catch { }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ApplyCriticalEffects error: {ex.Message}"); }
        }

        private static IEnumerator DeferredSetEmptyHands(Player player)
        {
            yield return null; 
            try { player?.SetEmptyHands(null); } catch (Exception ex) { Plugin.LogSource.LogWarning($"[DownedStateController] DeferredSetEmptyHands warn: {ex.Message}"); }
        }

        private static void ApplyRevivableState(Player player)
        {
            try
            {
                PlayerRestorations.SetAwarenessZero(player);
                Plugin.StaticCoroutineRunner.StartCoroutine(DeferredSetEmptyHands(player));

                var mc = player.MovementContext;
                mc.EnableSprint(false);
                mc.SetPoseLevel(0f, true);
                mc.IsInPronePose = true;

                if (player.ShouldVocalizeDeath(player.LastDamagedBodyPart))
                {
                    var trig = player.LastDamageType.IsWeaponInduced() ? EPhraseTrigger.OnDeath : EPhraseTrigger.OnAgony;
                    try { player.Speaker.Play(trig, player.HealthStatus, true, null); } catch { }
                }

                mc.ReleaseDoorIfInteractingWithOne();
                mc.OnStateChanged -= player.method_17;
                mc.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;

                if (mc.StationaryWeapon != null)
                {
                    mc.StationaryWeapon.Unlock(player.ProfileId);
                    if (mc.StationaryWeapon.Item == player.HandsController.Item)
                    {
                        mc.StationaryWeapon.Show();
                        player.ReleaseHand();
                    }
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ApplyRevivableState error: {ex.Message}"); }
        }

        //====================[ Self Revival ]====================
        private static void HandleSelfRevival_RequestSession(Player player, RMPlayer st)
        {
            if (!RevivalModSettings.SELF_REVIVAL_ENABLED.Value) return;
            if (!string.IsNullOrEmpty(st.CurrentReviverId) && st.CurrentReviverId != player.ProfileId) return;

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
                st.RevivePromptTimer = VFX_UI.ObjectivePanel(VFX_UI.Gradient(Color.blue, Color.green), VFX_UI.Position.BottomCenter, "Hold {0:F1}", 2f);
                VFX_UI.EnsureTransitPanelPosition();
            }
            else if (Input.GetKey(key) && st.SelfRevivalKeyHoldDuration.TryGetValue(key, out float holdTime))
            {
                holdTime += Time.deltaTime;
                
                if (holdTime >= 2f)
                {
                    st.SelfRevivalKeyHoldDuration.Remove(key);
                    st.ReviveRequestedSource = (int)ReviveSource.Self;

                    if (RevivalAuthority.TryAuthorizeReviveStart(player.ProfileId, player.ProfileId, "self", out var denyReason))
                    {
                        if (!RevivalModSettings.TESTING.Value) Utils.ConsumeDefibItem(player, Utils.GetDefib(player));
                        st.State = RMState.Reviving;
                        RMSession.UpdatePlayerState(player.ProfileId, st);
                        FikaBridge.SendSelfReviveStartPacket(player.ProfileId);
                    }
                    else
                    {
                        VFX_UI.Text(Color.yellow, string.IsNullOrEmpty(denyReason) ? "Revive denied by server" : denyReason);
                    }
                }
                else
                {
                    st.SelfRevivalKeyHoldDuration[key] = holdTime;
                }
            }
            else if (Input.GetKeyUp(key) && st.SelfRevivalKeyHoldDuration.Remove(key))
            {
                st.IsBeingRevived = false;
                st.IsPlayingRevivalAnimation = false;

                VFX_UI.HideObjectivePanel();
                st.RevivePromptTimer?.Stop(); st.RevivePromptTimer = null;

                if (Utils.HasDefib(player))
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, $"Revive! [{key}]");

                VFX_UI.Text(Color.yellow, "Self-revive canceled");
            }
        }

        private static void ObserveRevivingState(Player player, RMPlayer st)
        {
            if (st.State != RMState.Reviving || st.IsPlayingRevivalAnimation) return;

            st.IsPlayingRevivalAnimation = true;
            var source = (ReviveSource)st.ReviveRequestedSource;

            float duration = source == ReviveSource.Team ? RevivalModSettings.TEAMMATE_REVIVE_ANIMATION_DURATION.Value : RevivalModSettings.SELF_REVIVE_ANIMATION_DURATION.Value;

            st.CriticalStateMainTimer?.Stop();
            st.CriticalStateMainTimer = VFX_UI.TransitPanel(VFX_UI.Gradient(Color.red, Color.green), VFX_UI.Position.MiddleCenter, "REVIVING", duration);

            VFX_UI.HideObjectivePanel();
            st.RevivePromptTimer?.Stop(); st.RevivePromptTimer = null;

            if (source == ReviveSource.Team && player.IsYourPlayer)
                st.RevivePromptTimer = VFX_UI.ObjectivePanel(VFX_UI.Gradient(Color.blue, Color.green), VFX_UI.Position.BottomCenter, "Being Revived {0:F1}", duration);

            _ = MedicalAnimations.UseWithDuration(player, source == ReviveSource.Self ? MedicalAnimations.SurgicalItemType.SurvKit : MedicalAnimations.SurgicalItemType.CMS, duration);

            Plugin.StaticCoroutineRunner.StartCoroutine(DelayedActionAfterSeconds(duration, () => OnRevivalAnimationComplete(player)));
        }

        //====================[ Revival Completion ]====================
        private static void OnRevivalAnimationComplete(Player player)
        {
            if (player == null) return;
            var st = GetState(player);
            if (st.State != RMState.Reviving) return;

            var msg = (ReviveSource)st.ReviveRequestedSource == ReviveSource.Self 
                ? "Defibrillator used successfully! You are temporarily invulnerable but limited in movement." 
                : "Revived by teammate! You are temporarily invulnerable.";

            string reviverId = (ReviveSource)st.ReviveRequestedSource == ReviveSource.Self ? string.Empty : (st.CurrentReviverId ?? string.Empty);
            CompleteRevival(player, reviverId, msg);
        }

        public static bool StartTeammateRevive(string reviveeId, string reviverId = "")
        {
            try
            {
                if (!RMSession.IsPlayerCritical(reviveeId)) return false;

                var st = RMSession.GetPlayerState(reviveeId);

                // Guard against race condition: if player is mid-self-revive input, reject team revive
                if (st.SelfRevivalKeyHoldDuration.Count > 0 || (st.State == RMState.Reviving && st.ReviveRequestedSource == (int)ReviveSource.Self))
                {
                    Plugin.LogSource.LogWarning($"[Downed] teammate revive rejected for {reviveeId}: self-revive in progress");
                    return false;
                }
                
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
            if (player == null) return;
            CompleteRevival(player, GetState(player).CurrentReviverId ?? string.Empty, "Revived by teammate! You are temporarily invulnerable.");
        }

        private static void CompleteRevival(Player player, string reviverId, string message)
        {
            var st = GetState(player);
            st.State = RMState.Revived;
            RMSession.UpdatePlayerState(player.ProfileId, st);

            GodMode.ForceEnable(player);

            RevivalAuthority.NotifyReviveComplete(player.ProfileId, reviverId);
            FikaBridge.SendRevivedPacket(player.ProfileId, reviverId);
            GetState(player).ResyncCooldown = -1f; 
            FinishRevive(player, player.ProfileId, message);
        }

        //====================[ Post-Revival ]====================
        private static void FinishRevive(Player player, string playerId, string msg)
        {
            var st = RMSession.GetPlayerState(playerId);

            try { GhostMode.ExitGhostMode(player); } catch { }
            try { PlayerRestorations.RestoreDestroyedBodyParts(player); } catch (Exception ex) { Plugin.LogSource.LogError($"RestoreBodyHealth error: {ex.Message}"); }

            StartInvulnerabilityPeriod(player, st);

            st.IsPlayingRevivalAnimation = false;
            st.IsBeingRevived = false;
            st.KillOverride = false;
            st.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ClearTimers(st);

            RMSession.RemovePlayerFromCriticalPlayers(playerId);

            try { MedicalAnimations.CleanupAllFakeItems(player); } catch (Exception ex) { Plugin.LogSource.LogError($"CleanupFakeItems error: {ex.Message}"); }

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
                    var mc = player.MovementContext;
                    mc.IsInPronePose = false;
                    mc.SetPoseLevel(1f);
                    mc.EnableSprint(true);

                    try
                    {
                        mc.OnStateChanged -= player.method_17;
                        mc.OnStateChanged += player.method_17;
                        mc.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
                        mc.PhysicalConditionChanged += player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
                    }
                    catch (Exception ex) { Plugin.LogSource.LogWarning($"Re-hook movement events error: {ex.Message}"); }
                }

                if (player.IsYourPlayer)
                {
                    VFX_UI.HideTransitPanel();
                    st.CriticalStateMainTimer?.Stop(); st.CriticalStateMainTimer = null;
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, "Invulnerable {0:F1}", RevivalModSettings.REVIVAL_DURATION.Value);
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] StartInvulnerabilityPeriod error: {ex.Message}"); }
        }

        private static void EndInvulnerability(Player player)
        {
            var st = GetState(player);
            st.CurrentReviverId = string.Empty;

            // Only modify gameplay state on local player to avoid Fika desync; remote players receive authoritative state via resync packets.
            if (player.IsYourPlayer)
            {
                GodMode.Disable(player);
                RemoveRevivableState(player);
                PlayerRestorations.RestorePlayerMovement(player);
                st.OriginalMovementSpeed = -1f; // Reset so next critical entry re-captures current speed
            }

            st.State = RMState.CoolDown;
            st.CooldownTimer = RevivalModSettings.REVIVAL_COOLDOWN.Value;
            st.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (player.IsYourPlayer)
            {
                float cd = RevivalModSettings.REVIVAL_COOLDOWN.Value;
                RevivalAuthority.NotifyEndInvulnerability(player.ProfileId, cd);
                FikaBridge.SendPlayerStateResetPacket(player.ProfileId, isDead: false, cd);
                GetState(player).ResyncCooldown = -1f; 
                VFX_UI.HideObjectivePanel();
                VFX_UI.Text(Color.cyan, $"Invulnerability ended. Revival cooldown: {cd:F0}s");
            }
        }

        private static void RemoveRevivableState(Player player)
        {
            try { if (GetState(player).HasStoredAwareness) PlayerRestorations.RestoreAwareness(player); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] RemoveRevivableState error: {ex.Message}"); }
        }

        //====================[ Movement Helpers ]====================
        private static void ApplyDownedMovementSpeed(Player player, RMPlayer st)
        {
            try
            {
                bool frozen = st.State == RMState.Reviving || st.SelfRevivalKeyHoldDuration.Count > 0;
                float baseSpd = st.OriginalMovementSpeed > 0 ? st.OriginalMovementSpeed : player.Physical.WalkSpeedLimit;
                player.Physical.WalkSpeedLimit = frozen ? 0f : Mathf.Max(0.1f, baseSpd * (RevivalModSettings.DOWNED_MOVEMENT_SPEED.Value / 100f));
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

                if (st.State != RMState.Reviving && !st.IsBeingRevived) player.SetEmptyHands(null);
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ApplyDownedMovementRestrictions error: {ex.Message}"); }
        }

        //====================[ Queries ]====================
        public static bool IsRevivalOnCooldown(string playerId) => RMSession.GetPlayerState(playerId).State == RMState.CoolDown;
        
        public static bool IsPlayerInCriticalState(string playerId) => RMSession.HasPlayerState(playerId) && RMSession.GetPlayerState(playerId).IsCritical;
        
        public static bool IsPlayerInvulnerable(string playerId) => RMSession.HasPlayerState(playerId) && RMSession.GetPlayerState(playerId).IsInvulnerable;

        public static void SetPlayerCriticalState(Player player, bool critical, EDamageType damageType)
        {
            if (player == null) return;
            if (critical) EnterDowned(player, damageType);
            else ExitDowned(player);
        }
    }
}