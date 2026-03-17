//====================[ Imports ]====================
using System;
using System.Collections;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using KeepMeAlive.Components;
using KeepMeAlive.Fika;
using KeepMeAlive.Helpers;
using UnityEngine;

namespace KeepMeAlive.Features
{
    //====================[ RevivalController ]====================
    internal static class RevivalController
    {
        //====================[ State ]====================
        private static int _selfReviveTraceSequence;

        //====================[ Shared Revive Auth ]====================
        internal static IEnumerator AuthorizeReviveStartCoroutine(
            string playerId,
            string reviverId,
            ReviveSource source,
            Action<bool, string> onComplete)
        {
            bool allowed = true;
            string denyReason = string.Empty;

            string sourceName = source == ReviveSource.Self ? "self" : "team";
            var task = Task.Run(() =>
            {
                allowed = RevivePolicy.UseResilientAuthority(source)
                    ? RevivalAuthority.TryAuthorizeReviveStartResilient(playerId, reviverId, sourceName, out var reason)
                    : RevivalAuthority.TryAuthorizeReviveStart(playerId, reviverId, sourceName, out reason);
                denyReason = reason ?? string.Empty;
            });

            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                allowed = false;
                denyReason = "Revive authorization failed";
                Plugin.LogSource.LogWarning($"[ReviveAuth] Authorization task fault for target={playerId} reviver={reviverId} source={sourceName}: {task.Exception?.GetBaseException().Message}");
            }

            onComplete?.Invoke(allowed, denyReason);
        }

        internal static void SendReviveStartPacket(ReviveSource source, string playerId, string reviverId)
        {
            if (source == ReviveSource.Self)
            {
                FikaBridge.SendSelfReviveStartPacket(playerId);
            }
            else
            {
                FikaBridge.SendTeamReviveStartPacket(playerId, reviverId);
            }
        }

        internal static IEnumerator TeamReviveAuthStartCoroutine(
            Player reviver,
            string targetId,
            string reviverId,
            Func<bool> isAttemptCurrent)
        {
            bool allowed = false;
            string denyReason = string.Empty;

            yield return AuthorizeReviveStartCoroutine(targetId, reviverId, ReviveSource.Team, (ok, reason) =>
            {
                allowed = ok;
                denyReason = reason;
            });

            if (!isAttemptCurrent())
            {
                if (allowed)
                {
                    RevivalAuthority.NotifyBeginCritical(targetId);
                }

                Plugin.LogSource.LogInfo($"[ReviveAuth] Ignoring stale team auth result target={targetId} reviver={reviverId}");
                yield break;
            }

            var reviveeState = RMSession.GetPlayerState(targetId);
            if (reviveeState.State != RMState.BleedingOut && reviveeState.State != RMState.Reviving)
            {
                if (allowed)
                {
                    RevivalAuthority.NotifyBeginCritical(targetId);
                }

                yield break;
            }

            if (allowed)
            {
                if (!KeepMeAliveSettings.NO_DEFIB_REQUIRED.Value && RevivePolicy.ShouldConsumeDefib(ReviveSource.Team))
                {
                    var defib = Utils.GetDefib(reviver);
                    if (defib != null && !Utils.TryApplyItemLikeTeamHeal(reviver, defib, "TeamReviveDefib"))
                    {
                        Plugin.LogSource.LogWarning($"[TeamReviveDefib] ApplyItem did not consume defib for {reviver?.ProfileId}");
                    }
                }

                SendReviveStartPacket(ReviveSource.Team, targetId, reviverId);
                Plugin.LogSource.LogInfo($"Revive hold completed for {targetId}");
            }
            else
            {
                FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                VFX_UI.Text(Color.yellow, string.IsNullOrEmpty(denyReason) ? "Revive denied" : denyReason);
            }
        }

        //====================[ Silent Inventory Animation ]====================
        // Uses EFT's inventory-open hand state and network path without showing inventory UI or playing backpack UI sounds.
        internal static void StartSilentInventoryReviveAnimation(Player player, RMPlayer st, string reason)
        {
            if (player == null || st == null || !player.IsYourPlayer) return;

            try
            {
                // Blur is independent of weapon state — apply immediately.
                if (!st.IsSilentReviveBlurActive && CameraClass.Instance != null)
                {
                    CameraClass.Instance.Blur(true);
                    st.IsSilentReviveBlurActive = true;
                    ReviveDebug.Log("SilentInvAnim_BlurOn", player.ProfileId, player.IsYourPlayer, $"reason={reason}");
                }

                if (st.IsSilentInventoryAnimActive) return;

                // If a weapon is already in hands, still wait for it to settle for a few frames.
                if (HasWeaponInHands(player))
                {
                    Plugin.StaticCoroutineRunner.StartCoroutine(WaitForWeaponAndOpenSilentInventory(player, st, reason));
                    return;
                }

                // Hands are empty — temporarily allow the weapon-proceed block to pass through
                // so SetFirstAvailableItem can actually equip something, then open once a firearm
                // is really in hands.
                st.AllowWeaponEquipForReviveAnim = true;
                player.SetFirstAvailableItem((Result<IHandsController> _) =>
                {
                    try
                    {
                        if (player == null || st == null) return;
                        // Abort if the revive was cancelled before the callback fired.
                        if (st.State != RMState.Reviving || st.IsSilentInventoryAnimActive) return;

                        // Some callbacks fire before the hands controller settles.
                        // Wait until a firearm is really equipped before opening the inventory state.
                        Plugin.StaticCoroutineRunner.StartCoroutine(WaitForWeaponAndOpenSilentInventory(player, st, reason));
                    }
                    catch (Exception ex)
                    {
                        if (st != null) st.AllowWeaponEquipForReviveAnim = false;
                        Plugin.LogSource.LogError($"[ReviveAnim] WeaponEquipCallback error: {ex.Message}");
                    }
                });
                ReviveDebug.Log("SilentInvAnim_WaitingForEquip", player.ProfileId, player.IsYourPlayer, $"reason={reason}");
            }
            catch (Exception ex)
            {
                if (st != null) st.AllowWeaponEquipForReviveAnim = false;
                Plugin.LogSource.LogError($"[ReviveAnim] StartSilentInventoryReviveAnimation error: {ex.Message}");
            }
        }

        private static bool HasWeaponInHands(Player player)
        {
            return player?.HandsController?.Item is Weapon;
        }

        private static void OpenSilentInventory(Player player, RMPlayer st, string reason, string path)
        {
            if (player == null || st == null) return;
            if (st.State != RMState.Reviving || st.IsSilentInventoryAnimActive) return;
            if (!HasWeaponInHands(player)) return;

            player.SetInventoryOpened(true);
            st.IsSilentInventoryAnimActive = true;
            st.AllowWeaponEquipForReviveAnim = false;
            ReviveDebug.Log("SilentInvAnim_Open", player.ProfileId, player.IsYourPlayer, $"reason={reason} path={path}");
        }

        private static IEnumerator WaitForWeaponAndOpenSilentInventory(Player player, RMPlayer st, string reason)
        {
            const float timeoutSeconds = 2.0f;
            const int stableWeaponFramesRequired = 8;
            const float stableWeaponTimeRequired = 0.20f;
            float elapsed = 0f;
            int stableWeaponFrames = 0;
            float stableWeaponTime = 0f;

            while (elapsed < timeoutSeconds)
            {
                if (player == null || st == null)
                {
                    yield break;
                }

                if (st.State != RMState.Reviving || st.IsSilentInventoryAnimActive)
                {
                    st.AllowWeaponEquipForReviveAnim = false;
                    yield break;
                }

                if (HasWeaponInHands(player))
                {
                    stableWeaponFrames++;
                    stableWeaponTime += Time.deltaTime;
                    if (stableWeaponFrames >= stableWeaponFramesRequired && stableWeaponTime >= stableWeaponTimeRequired)
                    {
                        OpenSilentInventory(player, st, reason, "afterEquipWait_stable");
                        yield break;
                    }
                }
                else
                {
                    stableWeaponFrames = 0;
                    stableWeaponTime = 0f;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Avoid leaving pass-through enabled forever when no weapon can be equipped.
            st.AllowWeaponEquipForReviveAnim = false;
            ReviveDebug.Log("SilentInvAnim_EquipTimeout", player.ProfileId, player.IsYourPlayer, $"reason={reason}");
        }

        internal static void StopSilentInventoryReviveAnimation(Player player, RMPlayer st, string reason)
        {
            if (player == null || st == null || !player.IsYourPlayer) return;

            try
            {
                st.AllowWeaponEquipForReviveAnim = false;

                if (st.IsSilentInventoryAnimActive)
                {
                    player.SetInventoryOpened(false);
                    st.IsSilentInventoryAnimActive = false;
                    ReviveDebug.Log("SilentInvAnim_Close", player.ProfileId, player.IsYourPlayer, $"reason={reason}");
                }

                if (st.IsSilentReviveBlurActive && CameraClass.Instance != null)
                {
                    CameraClass.Instance.Blur(false);
                    st.IsSilentReviveBlurActive = false;
                    ReviveDebug.Log("SilentInvAnim_BlurOff", player.ProfileId, player.IsYourPlayer, $"reason={reason}");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[ReviveAnim] StopSilentInventoryReviveAnimation error: {ex.Message}");
            }
        }

        //====================[ Tracing ]====================
        internal static void TraceSelfRevive(Player player, RMPlayer st, string step, string details = null)
        {
            string id = player?.ProfileId ?? "<null>";
            int seq = ++_selfReviveTraceSequence;
            string state = st != null ? st.State.ToString() : "<null>";
            string reviver = st?.CurrentReviverId ?? string.Empty;
            int source = st?.ReviveRequestedSource ?? -1;
            bool beingRevived = st?.IsBeingRevived ?? false;
            bool selfReviving = st?.IsSelfReviving ?? false;
            bool anim = st?.IsReviveProgressActive ?? false;

            Plugin.LogSource.LogInfo(
                $"[SelfReviveTrace #{seq:000}] {id} step='{step}' state={state} source={source} reviver='{reviver}' beingRevived={beingRevived} selfReviving={selfReviving} anim={anim} {details ?? string.Empty}");
        }

        //====================[ Per-Frame Tick ]====================
        public static void TickSelfRevival(Player player, RMPlayer st)
        {
            if (!RevivePolicy.IsEnabled(ReviveSource.Self)) return;
            if (st.State != RMState.BleedingOut) return;
            if (!string.IsNullOrEmpty(st.CurrentReviverId) && st.CurrentReviverId != player.ProfileId) return;

            KeyCode key = KeepMeAliveSettings.SELF_REVIVAL_KEY.Value;
            float holdDuration = RevivePolicy.GetHoldDuration(ReviveSource.Self);

            if (Input.GetKeyDown(key))
            {
                TraceSelfRevive(player, st, "KeyDown", $"| key={key}");

                if (!KeepMeAliveSettings.NO_DEFIB_REQUIRED.Value && !Utils.HasDefib(player))
                {
                    TraceSelfRevive(player, st, "BlockedNoDefib", "| NO_DEFIB_REQUIRED=false and defib missing");
                    VFX_UI.Text(Color.red, "No defibrillator found! Unable to revive!");
                    return;
                }

                st.SelfReviveAttemptId++;
                st.SelfReviveHoldTime = 0f;
                st.SelfReviveCommitted = false;
                st.SelfReviveAuthPending = false;
                st.SelfRevivalKeyHoldDuration[key] = 0f;
                st.IsSelfReviving = true;

                TraceSelfRevive(player, st, "HoldStarted", $"| holdTarget={holdDuration:F2}s");

                VFX_UI.HideObjectivePanel();
                st.RevivePromptTimer = VFX_UI.ObjectivePanel(VFX_UI.Gradient(Color.blue, Color.green), VFX_UI.Position.BottomCenter, "Hold {0:F1}", holdDuration);
                VFX_UI.EnsureTransitPanelPosition();
            }
            else if (Input.GetKey(key) && st.IsSelfReviving)
            {
                if (st.SelfReviveCommitted || st.SelfReviveAuthPending)
                {
                    return;
                }

                float previous = st.SelfReviveHoldTime;
                st.SelfReviveHoldTime += Time.deltaTime;
                st.SelfRevivalKeyHoldDuration[key] = st.SelfReviveHoldTime;

                int prevBucket = Mathf.FloorToInt(previous * 4f);
                int nextBucket = Mathf.FloorToInt(Mathf.Min(st.SelfReviveHoldTime, holdDuration) * 4f);
                if (nextBucket > prevBucket)
                {
                    TraceSelfRevive(player, st, "Holding", $"| hold={st.SelfReviveHoldTime:F2}/{holdDuration:F2}");
                }

                if (st.SelfReviveHoldTime >= holdDuration)
                {
                    st.SelfReviveCommitted = true;
                    st.SelfReviveAuthPending = true;
                    st.ReviveRequestedSource = (int)ReviveSource.Self;
                    TraceSelfRevive(player, st, "HoldCompleted", "| threshold reached; begin auth");
                    BeginSelfReviveAuth(player, st, st.SelfReviveAttemptId);
                }
            }
            else if (Input.GetKeyUp(key) && st.IsSelfReviving)
            {
                if (st.SelfReviveCommitted || st.SelfReviveAuthPending)
                {
                    TraceSelfRevive(player, st, "KeyUpIgnoredAfterCommit", $"| key={key}");
                    return;
                }

                if (st.SelfReviveHoldTime > 0f)
                {
                    TraceSelfRevive(player, st, "KeyUpCanceled", $"| key={key}");
                    st.SelfReviveHoldTime = 0f;
                    st.SelfReviveCommitted = false;
                    st.SelfReviveAuthPending = false;
                    st.IsSelfReviving = false;
                    st.SelfRevivalKeyHoldDuration.Remove(key);
                    DownedStateController.CancelReviveState(player, st, "Self-revive canceled", Color.yellow);
                }
            }
        }

        //====================[ Auth Coroutine ]====================
        private static void BeginSelfReviveAuth(Player player, RMPlayer st, int attemptId)
        {
            TraceSelfRevive(player, st, "AuthBegin", $"| launching auth coroutine attempt={attemptId}");
            Plugin.StaticCoroutineRunner.StartCoroutine(SelfReviveAuthCoroutine(player, st, attemptId));
        }

        private static IEnumerator SelfReviveAuthCoroutine(Player player, RMPlayer st, int attemptId)
        {
            string pid = player.ProfileId;
            bool allowed = false;
            string denyReason = string.Empty;
            bool canCleanupAttempt = false;

            TraceSelfRevive(player, st, "AuthRequestStart", "| sending TryAuthorizeReviveStart(self)");

            yield return AuthorizeReviveStartCoroutine(pid, pid, ReviveSource.Self, (ok, reason) =>
            {
                allowed = ok;
                denyReason = reason;
            });

            TraceSelfRevive(player, st, "AuthRequestDone", $"| allowed={allowed} reason='{denyReason}'");

            if (st.SelfReviveAttemptId != attemptId)
            {
                if (allowed) RevivalAuthority.NotifyBeginCritical(pid);
                TraceSelfRevive(player, st, "AuthStale", $"| currentAttempt={st.SelfReviveAttemptId} resultAttempt={attemptId}");
                yield break;
            }

            canCleanupAttempt = true;

            if (st.State != RMState.BleedingOut || !st.IsSelfReviving || !st.SelfReviveAuthPending)
            {
                if (allowed) RevivalAuthority.NotifyBeginCritical(pid);
                st.SelfReviveAuthPending = false;
                st.SelfReviveCommitted = false;
                st.SelfReviveHoldTime = 0f;
                st.IsSelfReviving = false;
                TraceSelfRevive(player, st, "AuthAborted", "| state changed or no longer being revived while waiting");
                st.SelfRevivalKeyHoldDuration.Remove(KeepMeAliveSettings.SELF_REVIVAL_KEY.Value);
                yield break;
            }

            if (allowed)
            {
                if (!KeepMeAliveSettings.NO_DEFIB_REQUIRED.Value)
                {
                    TraceSelfRevive(player, st, "DefibCheck", "| NO_DEFIB_REQUIRED=false");
                    var defib = Utils.GetDefib(player);
                    if (defib == null)
                    {
                        st.SelfRevivalKeyHoldDuration.Remove(KeepMeAliveSettings.SELF_REVIVAL_KEY.Value);
                        st.SelfReviveAuthPending = false;
                        st.SelfReviveCommitted = false;
                        st.SelfReviveHoldTime = 0f;
                        st.IsSelfReviving = false;
                        TraceSelfRevive(player, st, "DefibMissingAfterAuth", "| canceling self-revive");
                        DownedStateController.CancelReviveState(player, st, "Defibrillator missing. Self-revive canceled.", Color.red);
                        yield break;
                    }

                    if (RevivePolicy.ShouldConsumeDefib(ReviveSource.Self))
                    {
                        TraceSelfRevive(player, st, "DefibApply", $"| itemId={defib.Id}");
                        if (!Utils.TryApplyItemLikeTeamHeal(player, defib, "SelfReviveDefib"))
                        {
                            Plugin.LogSource.LogWarning($"[SelfReviveDefib] ApplyItem did not consume defib for {player.ProfileId}");
                        }
                    }
                    else
                    {
                        TraceSelfRevive(player, st, "DefibConsumeSkipped", "| CONSUME_DEFIB_ON_SELF_REVIVE=false");
                    }
                }

                RMSession.SetPlayerState(pid, RMState.Reviving);
                st.IsReviveProgressActive = false;
                st.IsBeingRevived = false;
                if (st.ReviveProgressCoroutine != null)
                {
                    Plugin.StaticCoroutineRunner.StopCoroutine(st.ReviveProgressCoroutine);
                    st.ReviveProgressCoroutine = null;
                }
                st.SelfReviveAuthPending = false;
                st.SelfReviveCommitted = false;
                st.SelfReviveHoldTime = 0f;
                st.IsSelfReviving = false;
                st.CurrentReviverId = string.Empty;
                st.BeingRevivedWatchdogTimer = 0f;
                TraceSelfRevive(player, st, "StateSetReviving", "| sending SelfReviveStart packet");
                SendReviveStartPacket(ReviveSource.Self, pid, pid);
            }
            else
            {
                st.SelfReviveAuthPending = false;
                st.SelfReviveCommitted = false;
                st.SelfReviveHoldTime = 0f;
                st.IsSelfReviving = false;
                TraceSelfRevive(player, st, "AuthDenied", $"| reason='{denyReason}'");
                DownedStateController.CancelReviveState(player, st, string.IsNullOrEmpty(denyReason) ? "Revive denied by server" : denyReason, Color.yellow);
            }

            if (canCleanupAttempt)
            {
                st.SelfRevivalKeyHoldDuration.Remove(KeepMeAliveSettings.SELF_REVIVAL_KEY.Value);
            }
            TraceSelfRevive(player, st, "AuthEnd", "| hold dictionary cleanup complete");
        }

        //====================[ Reviving State Observer ]====================
        public static void ObserveRevivingState(Player player, RMPlayer st)
        {
            ReviveDebug.Log("ObserveRevive_Enter", player.ProfileId, player.IsYourPlayer, $"state={st.State} anim={st.IsReviveProgressActive}");
            if (st.State != RMState.Reviving)
            {
                ReviveDebug.Log("ObserveRevive_SkipState", player.ProfileId, player.IsYourPlayer, $"state={st.State}");
                return;
            }

            if (st.IsReviveProgressActive)
            {
                if (st.ReviveProgressCoroutine == null)
                {
                    ReviveDebug.Log("ObserveRevive_ResetStaleAnimFlag", player.ProfileId, player.IsYourPlayer, null);
                    st.IsReviveProgressActive = false;
                }
                else
                {
                    ReviveDebug.Log("ObserveRevive_SkipAlreadyPlaying", player.ProfileId, player.IsYourPlayer, null);
                    return;
                }
            }

            st.IsReviveProgressActive = true;
            var source = (ReviveSource)st.ReviveRequestedSource;

            StartSilentInventoryReviveAnimation(player, st, "ObserveRevivingState");

            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "ObserveRevivingState", "| entering self revive progress path");
            }

            float duration = RevivePolicy.GetProgressDuration(source);

            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "ReviveDurationConfig", $"| duration={duration:F2}");
            }

            st.CriticalStateMainTimer?.Stop();
            st.CriticalStateMainTimer = VFX_UI.TransitPanel(VFX_UI.Gradient(Color.red, Color.green), VFX_UI.Position.MiddleCenter, "REVIVING", duration);

            VFX_UI.HideObjectivePanel();
            DownedStateController.ClearRevivePromptTimer(st);

            ReviveDebug.Log("ObserveRevive_StartProgress", player.ProfileId, player.IsYourPlayer, $"duration={duration:F2}");
            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "ReviveProgressStart", $"| duration={duration:F2}");
            }

            int expectedCycle = st.ReviveCycleId;
            st.ReviveProgressCoroutine = Plugin.StaticCoroutineRunner.StartCoroutine(DownedStateController.DelayedActionAfterSeconds(duration, () => OnReviveProgressComplete(player, expectedCycle)));
            ReviveDebug.Log("ObserveRevive_CoroutineStarted", player.ProfileId, player.IsYourPlayer, $"delay={duration:F2} cycle={expectedCycle}");
            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "ReviveProgressScheduled", $"| delay={duration:F2}");
            }
        }

        //====================[ Revival Completion ]====================
        private static void OnReviveProgressComplete(Player player, int expectedCycle)
        {
            if (player == null) return;
            var st = RMSession.GetPlayerState(player.ProfileId);
            ReviveDebug.Log("RevivalComplete_Enter", player.ProfileId, player.IsYourPlayer, $"state={st.State} cycle={st.ReviveCycleId} expected={expectedCycle}");
            if ((ReviveSource)st.ReviveRequestedSource == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "ReviveProgressCompleteCallback", $"| callback fired (expected {expectedCycle}, current {st.ReviveCycleId})");
            }

            if (st.ReviveCycleId != expectedCycle)
            {
                ReviveDebug.Log("RevivalComplete_SkipCycle", player.ProfileId, player.IsYourPlayer, $"stale cycle");
                return;
            }

            if (st.State != RMState.Reviving)
            {
                ReviveDebug.Log("RevivalComplete_SkipState", player.ProfileId, player.IsYourPlayer, $"state={st.State}");
                if ((ReviveSource)st.ReviveRequestedSource == ReviveSource.Self)
                {
                    TraceSelfRevive(player, st, "ReviveProgressIgnored", "| state is no longer Reviving");
                }
                return;
            }

            st.ReviveProgressCoroutine = null;

            var msg = (ReviveSource)st.ReviveRequestedSource == ReviveSource.Self
                ? "Defibrillator used successfully! You are temporarily invulnerable."
                : "Revived by teammate! You are temporarily invulnerable.";

            string reviverId = (ReviveSource)st.ReviveRequestedSource == ReviveSource.Self ? string.Empty : (st.CurrentReviverId ?? string.Empty);
            ReviveDebug.Log("RevivalComplete_CallCompleteRevival", player.ProfileId, player.IsYourPlayer, $"reviverId={reviverId}");
            StopSilentInventoryReviveAnimation(player, st, "ReviveProgressComplete");
            if ((ReviveSource)st.ReviveRequestedSource == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "ReviveProgressProceed", "| calling CompleteRevival");
            }
            CompleteRevival(player, reviverId, msg);
        }

        private static void CompleteRevival(Player player, string reviverId, string message)
        {
            var st = RMSession.GetPlayerState(player.ProfileId);
            var source = (ReviveSource)st.ReviveRequestedSource;
            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "CompleteRevivalBegin", $"| reviverId='{reviverId}'");
            }

            RMSession.SetPlayerState(player.ProfileId, RMState.Revived);
            RMSession.UpdatePlayerState(player.ProfileId, st);

            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "CompleteRevivalStateSet", "| state set to Revived");
            }

            GodMode.ForceEnable(player);

            FikaBridge.SendRevivedPacket(player.ProfileId, reviverId);
            st.ResyncCooldown = -1f;
            RevivalAuthority.NotifyReviveComplete(player.ProfileId, reviverId);
            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "CompleteRevivalNetwork", "| sent Revived packet and notified authority");
            }
            FinishRevive(player, player.ProfileId, message, "LocalFinishRevive");
        }

        internal static void FinalizeRevivalFromPacket(Player player, string playerId, string reviverId)
        {
            var source = string.IsNullOrEmpty(reviverId) || reviverId == playerId ? ReviveSource.Self : ReviveSource.Team;
            var msg = source == ReviveSource.Self
                ? "Defibrillator used successfully! You are temporarily invulnerable."
                : "Revived by teammate! You are temporarily invulnerable.";

            FinishRevive(player, playerId, msg, "RevivedPacket", reviverId);
        }

        private static void FinishRevive(Player player, string playerId, string msg, string finalizeSource, string reviverId = null)
        {
            var st = RMSession.GetPlayerState(playerId);
            if (!string.IsNullOrEmpty(reviverId))
            {
                var sourceFromReviver = reviverId == playerId ? ReviveSource.Self : ReviveSource.Team;
                st.ReviveRequestedSource = (int)sourceFromReviver;
                if (sourceFromReviver == ReviveSource.Team)
                {
                    st.CurrentReviverId = reviverId;
                }
            }

            RMSession.SetPlayerState(playerId, RMState.Revived);
            RMSession.UpdatePlayerState(playerId, st);

            bool applyFinalize = player.IsYourPlayer && DownedStateController.TryCommitReviveFinalizeForCycle(finalizeSource, playerId, st);
            if (player.IsYourPlayer && !applyFinalize)
            {
                return;
            }

            var source = (ReviveSource)st.ReviveRequestedSource;
            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "FinishReviveBegin", "| applying post-revive effects");
            }

            PostRevivalController.BeginPostRevival(player, playerId, st, applyFinalize);

            st.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            DownedStateController.ClearTimers(st);

            VFX_UI.Text(Color.green, msg);
            Plugin.LogSource.LogInfo($"[Downed] revive complete for {playerId}");

            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "FinishReviveEnd", "| revive complete UI shown");
            }
        }
    }
}
