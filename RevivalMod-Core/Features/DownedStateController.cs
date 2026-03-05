//====================[ Imports ]====================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        //====================[ Fields ]====================

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

                // Bug fix: also force-enable during the Revived (invuln) window.
                // After CompleteRevival sets State = Revived, the isCritical check becomes
                // false before PostReviveEffects has finished restoring HP. On the next tick,
                // if isInjured reads false (Fika HealthSync delay or no destroyed parts),
                // the collider gets disabled and will never re-enable. Treating Revived the
                // same as critical ensures the Heal interactable is always accessible
                // immediately after revival.
                var pst = RMSession.GetPlayerState(player.ProfileId);
                bool isRevived = pst?.State == RMState.Revived;

                bool isInjured = false;

                if (!isCritical && !isRevived)
                {
                    for (int i = 0; i < TrackedBodyParts.Length; i++)
                    {
                        var hp = player.HealthController.GetBodyPartHealth(TrackedBodyParts[i]);
                        if (hp.Current >= hp.Maximum) continue;
                        isInjured = true;
                        break;
                    }
                }

                bool shouldEnable = isCritical || isRevived || isInjured;

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
                st.ReviveRequestedSource = 0;
                // Reset animation and session flags so stale state from a previous
                // downed session can never corrupt the new one.
                st.IsPlayingRevivalAnimation = false;
                st.IsBeingRevived = false;
                st.SelfRevivalKeyHoldDuration.Clear();
                st.CurrentReviverId = string.Empty;

                // Bug fix #1: If a MedPickerInteractable was open when this player went down,
                // force-close it so HasActivePicker never permanently blocks the Heal option
                // after revival.
                if (_bodyInteractableCache.TryGetValue(id, out var cachedBi) && cachedBi != null)
                    cachedBi.ForceClosePicker();

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

            // Cancel any in-flight revival animation coroutine so it cannot fire for
            // a stale session if the player re-enters downed state immediately after.
            if (st.ReviveAnimationCoroutine != null)
            {
                Plugin.StaticCoroutineRunner.StopCoroutine(st.ReviveAnimationCoroutine);
                st.ReviveAnimationCoroutine = null;
            }

            st.State = RMState.None;
            st.IsPlayingRevivalAnimation = false;
            st.IsBeingRevived = false;
            st.SelfRevivalKeyHoldDuration.Clear();
            st.CurrentReviverId = string.Empty;
            st.ReviveRequestedSource = 0;
            HideAllPanelsAndStop(st);

            // Always restore movement hooks and limits. If invulnerability was active, force-end it
            // without broadcasting — TickInvulnerability won't fire because State is now None.
            st.InvulnerabilityTimer = 0f;
            RemoveRevivableState(player);
            PlayerRestorations.RestorePlayerMovement(player);
            st.OriginalMovementSpeed = -1f;

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

            // #1 Watchdog: clear stale IsBeingRevived if the reviver never followed through (disconnect/crash).
            // Only applies to team-initiated revives (CurrentReviverId is set by OnTeamHelpPacketReceived).
            // Self-revive sets IsBeingRevived without setting CurrentReviverId and never initialises
            // BeingRevivedWatchdogTimer, so the default 0f value would fire the timeout immediately.
            if (st.IsBeingRevived && st.State == RMState.BleedingOut && !string.IsNullOrEmpty(st.CurrentReviverId))
            {
                st.BeingRevivedWatchdogTimer -= Time.deltaTime;
                if (st.BeingRevivedWatchdogTimer <= 0f)
                {
                    Plugin.LogSource.LogWarning($"[Downed] Reviver watchdog expired for {player.ProfileId}; clearing IsBeingRevived");
                    st.IsBeingRevived = false;
                    st.CurrentReviverId = string.Empty;
                    VFX_UI.HideObjectivePanel();
                    st.RevivePromptTimer?.Stop(); st.RevivePromptTimer = null;
                    if (RevivalModSettings.SELF_REVIVAL_ENABLED.Value && Utils.HasDefib(player))
                        VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, $"Revive! [{RevivalModSettings.SELF_REVIVAL_KEY.Value}]");
                    VFX_UI.Text(Color.yellow, "Reviver disconnected or timed out.");
                }
            }

            if (st.State == RMState.BleedingOut && !st.IsBeingRevived && (st.CriticalTimer <= 0f || Input.GetKeyDown(RevivalModSettings.GIVE_UP_KEY.Value)))
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
            // Guard: don't recreate a timer that would instantly expire — the death check fires anyway.
            if (st.CriticalTimer <= 0.5f) return;
            st.CriticalStateMainTimer = VFX_UI.TransitPanel(VFX_UI.Gradient(Color.red, Color.black), VFX_UI.Position.MiddleCenter, "BLEEDING OUT", st.CriticalTimer);
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

                if (holdTime >= 2f && holdTime < float.MaxValue)
                {
                    // Mark as auth-pending (float.MaxValue sentinel) so this block doesn't fire again
                    // on subsequent ticks while the off-thread auth coroutine is running.
                    st.SelfRevivalKeyHoldDuration[key] = float.MaxValue;
                    st.ReviveRequestedSource = (int)ReviveSource.Self;
                    BeginSelfReviveAuth(player, st); // commits State in coroutine; non-blocking
                }
                else if (holdTime < 2f)
                {
                    st.SelfRevivalKeyHoldDuration[key] = holdTime;
                }
                // holdTime == float.MaxValue: auth is in-flight, key still held — nothing to do
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

        //====================[ Self-Revival Auth (off-thread) ]====================
        // Kicks off TryAuthorizeReviveStart on a background Task so the HTTP call never
        // blocks the Unity main thread. State is committed back on the main thread via coroutine.
        private static void BeginSelfReviveAuth(Player player, RMPlayer st)
        {
            Plugin.StaticCoroutineRunner.StartCoroutine(SelfReviveAuthCoroutine(player, st));
        }

        private static IEnumerator SelfReviveAuthCoroutine(Player player, RMPlayer st)
        {
            string pid = player.ProfileId;
            bool allowed = true;
            string denyReason = string.Empty;

            // Run blocking HTTP call off the main thread.
            var task = Task.Run(() =>
            {
                allowed    = RevivalAuthority.TryAuthorizeReviveStart(pid, pid, "self", out var reason);
                denyReason = reason ?? string.Empty;
            });

            while (!task.IsCompleted) yield return null;

            // If the player gave up, pressed give-up, or died while auth was pending, discard.
            if (st.State != RMState.BleedingOut || !st.IsBeingRevived)
                yield break;

            if (allowed)
            {
                if (!RevivalModSettings.TESTING.Value) Utils.ConsumeDefibItem(player, Utils.GetDefib(player));
                st.State = RMState.Reviving;
                FikaBridge.SendSelfReviveStartPacket(pid);
            }
            else
            {
                st.IsBeingRevived = false;
                VFX_UI.HideObjectivePanel();
                st.RevivePromptTimer?.Stop(); st.RevivePromptTimer = null;
                KeyCode key = RevivalModSettings.SELF_REVIVAL_KEY.Value;
                if (Utils.HasDefib(player))
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, $"Revive! [{key}]");
                VFX_UI.Text(Color.yellow, string.IsNullOrEmpty(denyReason) ? "Revive denied by server" : denyReason);
            }

            // Remove the float.MaxValue sentinel so the key-hold dict is clean.
            st.SelfRevivalKeyHoldDuration.Remove(RevivalModSettings.SELF_REVIVAL_KEY.Value);
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

            // Bug fix #3: The TransitPanel (center) already shows the revive countdown.
            // A second ObjectivePanel (bottom) saying "Being Revived X.Xs" is redundant and
            // was shown on top of the earlier "X is reviving you" message — removed.

            _ = MedicalAnimations.UseWithDuration(player, source == ReviveSource.Self ? MedicalAnimations.SurgicalItemType.SurvKit : MedicalAnimations.SurgicalItemType.CMS, duration);

            st.ReviveAnimationCoroutine = Plugin.StaticCoroutineRunner.StartCoroutine(DelayedActionAfterSeconds(duration, () => OnRevivalAnimationComplete(player)));
        }

        //====================[ Revival Completion ]====================
        private static void OnRevivalAnimationComplete(Player player)
        {
            if (player == null) return;
            var st = GetState(player);
            if (st.State != RMState.Reviving) return;

            st.ReviveAnimationCoroutine = null; // natural completion; clear the handle

            var msg = (ReviveSource)st.ReviveRequestedSource == ReviveSource.Self 
                ? "Defibrillator used successfully! You are temporarily invulnerable but limited in movement." 
                : "Revived by teammate! You are temporarily invulnerable.";

            string reviverId = (ReviveSource)st.ReviveRequestedSource == ReviveSource.Self ? string.Empty : (st.CurrentReviverId ?? string.Empty);
            CompleteRevival(player, reviverId, msg);
        }

        private static void CompleteRevival(Player player, string reviverId, string message)
        {
            var st = GetState(player);
            st.State = RMState.Revived;
            RMSession.UpdatePlayerState(player.ProfileId, st);

            GodMode.ForceEnable(player);

            // Broadcast to peers FIRST so remote clients transition out of Reviving
            // before the fire-and-forget HTTP notify goes out.
            FikaBridge.SendRevivedPacket(player.ProfileId, reviverId);
            st.ResyncCooldown = -1f;
            RevivalAuthority.NotifyReviveComplete(player.ProfileId, reviverId);
            FinishRevive(player, player.ProfileId, message);
        }

        //====================[ Post-Revival ]====================
        private static void FinishRevive(Player player, string playerId, string msg)
        {
            var st = RMSession.GetPlayerState(playerId);

            try { GhostMode.ExitGhostMode(player); } catch { }
            try { PostReviveEffects.Apply(player, (ReviveSource)st.ReviveRequestedSource); } catch (Exception ex) { Plugin.LogSource.LogError($"PostReviveEffects error: {ex.Message}"); }

            // Clear animation/revive flags BEFORE restoring movement so no re-entrant tick
            // or event callback inside StartInvulnerabilityPeriod reads stale values (#8).
            st.IsPlayingRevivalAnimation = false;
            st.IsBeingRevived = false;
            st.CurrentReviverId = string.Empty; // clear early so a stale reviver ID can't block self-revive if state re-corrupts (#13)
            st.KillOverride = false;
            StartInvulnerabilityPeriod(player, st);

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
                st.InvulnerabilityTimer = PostReviveEffects.GetInvulnDuration((ReviveSource)st.ReviveRequestedSource);
                if (st.OriginalMovementSpeed > 0) player.Physical.WalkSpeedLimit = st.OriginalMovementSpeed;

                if (player.MovementContext != null)
                {
                    var mc = player.MovementContext;
                    mc.IsInPronePose = false;
                    // Bug fix #2: Use the force/immediate flag (true) to match the forced
                    // SetPoseLevel(0f, true) applied every tick during the downed state.
                    // Without it, EFT's movement state machine processes the queued forced-prone
                    // after the unforced stand-up, causing the visible dip back to prone.
                    mc.SetPoseLevel(1f, true);
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
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, "Invulnerable {0:F1}", PostReviveEffects.GetInvulnDuration((ReviveSource)st.ReviveRequestedSource));
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
            float cd = PostReviveEffects.GetCooldownDuration((ReviveSource)st.ReviveRequestedSource);
            st.CooldownTimer = cd;
            st.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (player.IsYourPlayer)
            {
                RevivalAuthority.NotifyEndInvulnerability(player.ProfileId, cd);
                FikaBridge.SendPlayerStateResetPacket(player.ProfileId, isDead: false, cd);
                st.ResyncCooldown = -1f;
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
                // Also freeze while IsBeingRevived is active: covers team-help window and self-revive auth-pending gap.
                bool frozen = st.State == RMState.Reviving || st.SelfRevivalKeyHoldDuration.Count > 0 || st.IsBeingRevived;
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