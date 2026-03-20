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
            ReviveDebug.Log("AuthCoroutine_Start", playerId, false, $"reviver={reviverId} source={sourceName}");
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
                denyReason = PlayerFacingMessages.ReviveDenied.AuthorizationFailed;
                Plugin.LogSource.LogWarning($"[ReviveAuth] Authorization task fault for target={playerId} reviver={reviverId} source={sourceName}: {task.Exception?.GetBaseException().Message}");
            }

            ReviveDebug.Log("AuthCoroutine_Done", playerId, false, $"reviver={reviverId} source={sourceName} allowed={allowed} reason='{denyReason}'");
            onComplete?.Invoke(allowed, denyReason);
        }

        internal static void SendReviveStartPacket(ReviveSource source, string playerId, string reviverId)
        {
            ReviveDebug.Log("SendReviveStartPacket", playerId, false, $"source={source} reviver={reviverId}");
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
            ReviveDebug.Log("TeamAuthCoroutine_Start", targetId, false, $"reviver={reviverId}");
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

                ReviveDebug.Log("TeamAuthCoroutine_StaleAttempt", targetId, false, $"reviver={reviverId} allowed={allowed}");
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

                ReviveDebug.Log("TeamAuthCoroutine_SkipState", targetId, false, $"reviver={reviverId} state={reviveeState.State}");
                yield break;
            }

            if (allowed)
            {
                ReviveDebug.Log("TeamAuthCoroutine_Allowed", targetId, false, $"reviver={reviverId}");
                if (!KeepMeAliveSettings.NO_REVIVE_ITEM_REQUIRED.Value && RevivePolicy.ShouldConsumeReviveItem(ReviveSource.Team))
                {
                    var reviveItem = Utils.GetReviveItem(reviver);
                    if (reviveItem != null && !Utils.TryConsumeReviveItem(reviver, reviveItem, "TeamReviveReviveItem"))
                    {
                        Plugin.LogSource.LogWarning($"[TeamReviveReviveItem] Failed to consume reviveItem for {reviver?.ProfileId}");
                    }
                }

                SendReviveStartPacket(ReviveSource.Team, targetId, reviverId);
                Plugin.LogSource.LogInfo($"Revive hold completed for {targetId}");
            }
            else
            {
                ReviveDebug.Log("TeamAuthCoroutine_Denied", targetId, false, $"reviver={reviverId} reason='{denyReason}'");
                FikaBridge.SendTeamCancelPacket(targetId, reviverId);
                VFX_UI.Text(Color.yellow, string.IsNullOrEmpty(denyReason) ? PlayerFacingMessages.ReviveDenied.TeamFallback : denyReason);
            }
        }

        //====================[ Silent Inventory Animation ]====================
        private static bool HasWeaponInHands(Player player)
        {
            return player?.HandsController?.Item is Weapon;
        }

internal static void StopSilentInventoryReviveAnimation(Player player, RMPlayer st, string reason)
        {
            if (player == null || st == null || !player.IsYourPlayer) return;
            ReviveDebug.Log("StopSilentInvAnim_Enter", player.ProfileId, player.IsYourPlayer, $"reason={reason} invActive={st.IsSilentInventoryAnimActive} blurActive={st.IsSilentReviveBlurActive}");

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

        //====================[ Revive Orchestration ]====================
        internal static void ApplyKnockOut(Player player)
        {
            if (player == null || !player.IsYourPlayer) return;
            ReviveDebug.Log("ApplyKnockOut_Enter", player.ProfileId, player.IsYourPlayer, null);

            try
            {
                var st = RMSession.GetPlayerState(player.ProfileId);

                // Freeze movement immediately on revive entry.
                if (player.Physical != null)
                {
                    player.Physical.WalkSpeedLimit = 0f;
                }

                // Reuse the same blur behavior used by the silent inventory revive animation.
                if (!st.IsSilentReviveBlurActive && CameraClass.Instance != null)
                {
                    CameraClass.Instance.Blur(true);
                    st.IsSilentReviveBlurActive = true;
                    ReviveDebug.Log("KnockOut_BlurOn", player.ProfileId, player.IsYourPlayer, null);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[ReviveAnim] ApplyKnockOut error: {ex.Message}");
            }
        }

        private static IEnumerator ApplyReviveEffectsCoroutine(Player player, RMPlayer st)
        {
            if (player == null || st == null) yield break;
            if (!player.IsYourPlayer) yield break;
            if (st.State != RMState.Reviving) yield break;

            ReviveDebug.Log("ReviveEffects_Start", player.ProfileId, player.IsYourPlayer, $"state={st.State} source={(ReviveSource)st.ReviveRequestedSource}");
            if (KeepMeAliveSettings.BLOCK_UI_WHEN_DOWNED.Value) DownedUiBlocker.SetBlocked(true);

            if (!st.IsSilentReviveBlurActive && CameraClass.Instance != null)
            {
                CameraClass.Instance.Blur(true);
                st.IsSilentReviveBlurActive = true;
            }

            //====================[ 1. Gun In Hand ]====================
            ReviveDebug.Log("ReviveEffects_GunEquip", player.ProfileId, player.IsYourPlayer, null);
            st.AllowWeaponEquipForReviveAnim = true;
            player.SetFirstAvailableItem((Result<IHandsController> _) => { });

            //====================[ 2. Wait 1s Then Open Inventory ]====================
            yield return new WaitForSeconds(1.0f);
            ReviveDebug.Log("ReviveEffects_OpenInventory", player.ProfileId, player.IsYourPlayer, null);
            player.SetInventoryOpened(true);
            st.IsSilentInventoryAnimActive = true;
            st.AllowWeaponEquipForReviveAnim = false;

            //====================[ 3. Wait 1s Then Consume Item ]====================
            yield return new WaitForSeconds(1.0f);
            ReviveDebug.Log("ReviveEffects_ConsumeStep", player.ProfileId, player.IsYourPlayer, $"source={(ReviveSource)st.ReviveRequestedSource}");
            var source = (ReviveSource)st.ReviveRequestedSource;
            if (source == ReviveSource.Self && !KeepMeAliveSettings.NO_REVIVE_ITEM_REQUIRED.Value && RevivePolicy.ShouldConsumeReviveItem(ReviveSource.Self))
            {
                var reviveItem = Utils.GetReviveItem(player);
                ReviveDebug.Log("ReviveEffects_ConsumeItem", player.ProfileId, player.IsYourPlayer, $"itemFound={reviveItem != null}");
                if (reviveItem != null)
                {
                    Utils.TryConsumeReviveItem(player, reviveItem, "SelfReviveReviveItem");
                }
            }
            ReviveDebug.Log("ReviveEffects_Done", player.ProfileId, player.IsYourPlayer, null);
        }

        internal static void StartRevive(Player player, RMPlayer st, string reason)
        {
            if (player == null || st == null) return;
            if (!player.IsYourPlayer)
            {
                ReviveDebug.Log("StartRevive_SkipNonLocal", player.ProfileId, player.IsYourPlayer, $"reason={reason}");
                return;
            }
            if (st.State != RMState.Reviving) return;

            ReviveDebug.Log("StartRevive_Enter", player.ProfileId, player.IsYourPlayer, $"reason={reason} source={(ReviveSource)st.ReviveRequestedSource}");

            // Prevent duplicate re-entry from replayed/looped packets while revive progress is already active.
            if (st.IsReviveProgressActive && st.ReviveProgressCoroutine != null)
            {
                ReviveDebug.Log("StartRevive_SkipDuplicate", player.ProfileId, player.IsYourPlayer, $"reason={reason}");
                return;
            }

            if (st.ReviveProgressCoroutine != null)
            {
                Plugin.StaticCoroutineRunner.StopCoroutine(st.ReviveProgressCoroutine);
                st.ReviveProgressCoroutine = null;
            }

            st.IsReviveProgressActive = true;
            ApplyKnockOut(player);
            Plugin.StaticCoroutineRunner.StartCoroutine(ApplyReviveEffectsCoroutine(player, st));

            var source = (ReviveSource)st.ReviveRequestedSource;
            float duration = RevivePolicy.GetProgressDuration(source);

            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "StartRevive", $"| duration={duration:F2} reason={reason}");
            }

            st.CriticalStateMainTimer?.Stop();
            st.CriticalStateMainTimer = VFX_UI.TransitPanel(VFX_UI.Gradient(Color.red, Color.green), VFX_UI.Position.MiddleCenter, PlayerFacingMessages.Revive.RevivingProgress, duration);

            VFX_UI.HideObjectivePanel();
            DownedStateController.ClearRevivePromptTimer(st);

            int expectedCycle = st.ReviveCycleId;
            st.ReviveProgressCoroutine = Plugin.StaticCoroutineRunner.StartCoroutine(DownedStateController.DelayedActionAfterSeconds(duration, () => OnReviveProgressComplete(player, expectedCycle)));
            ReviveDebug.Log("StartRevive_Scheduled", player.ProfileId, player.IsYourPlayer, $"duration={duration:F2} cycle={expectedCycle}");
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

                if (!KeepMeAliveSettings.NO_REVIVE_ITEM_REQUIRED.Value && !Utils.HasReviveItem(player))
                {
                    TraceSelfRevive(player, st, "BlockedNoReviveItem", "| NO_REVIVE_ITEM_REQUIRED=false and reviveItem missing");
                    VFX_UI.Text(Color.red, PlayerFacingMessages.Revive.NoReviveItemFound);
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
                st.RevivePromptTimer = VFX_UI.ObjectivePanel(VFX_UI.Gradient(Color.blue, Color.green), VFX_UI.Position.BottomCenter, PlayerFacingMessages.Revive.HoldObjective, holdDuration);
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
                    DownedStateController.CancelReviveState(player, st, PlayerFacingMessages.Revive.SelfReviveCanceled, Color.yellow);
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
                if (!KeepMeAliveSettings.NO_REVIVE_ITEM_REQUIRED.Value)
                {
                    TraceSelfRevive(player, st, "ReviveItemCheck", "| NO_REVIVE_ITEM_REQUIRED=false");
                    var reviveItem = Utils.GetReviveItem(player);
                    if (reviveItem == null)
                    {
                        st.SelfRevivalKeyHoldDuration.Remove(KeepMeAliveSettings.SELF_REVIVAL_KEY.Value);
                        st.SelfReviveAuthPending = false;
                        st.SelfReviveCommitted = false;
                        st.SelfReviveHoldTime = 0f;
                        st.IsSelfReviving = false;
                        TraceSelfRevive(player, st, "ReviveItemMissingAfterAuth", "| canceling self-revive");
                        DownedStateController.CancelReviveState(player, st, PlayerFacingMessages.Revive.MissingItemCanceled, Color.red);
                        yield break;
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
                DownedStateController.CancelReviveState(player, st, string.IsNullOrEmpty(denyReason) ? PlayerFacingMessages.ReviveDenied.SelfFallback : denyReason, Color.yellow);
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
            // One-shot startup is now handled by state transition detection in DownedStateController.TickDowned.
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
                ? PlayerFacingMessages.ReviveComplete.LocalSelf
                : PlayerFacingMessages.ReviveComplete.LocalTeam;

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
            ReviveDebug.Log("CompleteRevival_Enter", player.ProfileId, player.IsYourPlayer, $"reviver={reviverId}");
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
            ReviveDebug.Log("CompleteRevival_GodModeOn", player.ProfileId, player.IsYourPlayer, null);

            FikaBridge.SendRevivedPacket(player.ProfileId, reviverId);
            st.ResyncCooldown = -1f;
            RevivalAuthority.NotifyReviveComplete(player.ProfileId, reviverId);
            ReviveDebug.Log("CompleteRevival_NetworkDone", player.ProfileId, player.IsYourPlayer, $"reviver={reviverId}");
            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "CompleteRevivalNetwork", "| sent Revived packet and notified authority");
            }
            FinishRevive(player, player.ProfileId, message, "LocalFinishRevive");
        }

        internal static void FinalizeRevivalFromPacket(Player player, string playerId, string reviverId)
        {
            ReviveDebug.Log("FinalizeFromPacket_Enter", playerId, player.IsYourPlayer, $"reviver={reviverId}");
            var source = string.IsNullOrEmpty(reviverId) || reviverId == playerId ? ReviveSource.Self : ReviveSource.Team;
            var msg = source == ReviveSource.Self
                ? PlayerFacingMessages.ReviveComplete.LocalSelf
                : PlayerFacingMessages.ReviveComplete.LocalTeam;

            FinishRevive(player, playerId, msg, "RevivedPacket", reviverId);
        }

        private static void FinishRevive(Player player, string playerId, string msg, string finalizeSource, string reviverId = null)
        {
            ReviveDebug.Log("FinishRevive_Enter", playerId, player.IsYourPlayer, $"finalizeSource={finalizeSource} reviver={reviverId ?? "<null>"}");
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
            ReviveDebug.Log("FinishRevive_FinalizeCheck", playerId, player.IsYourPlayer, $"applyFinalize={applyFinalize} isLocal={player.IsYourPlayer}");
            if (player.IsYourPlayer && !applyFinalize)
            {
                ReviveDebug.Log("FinishRevive_SkipDuplicate", playerId, player.IsYourPlayer, "already finalized this cycle");
                return;
            }

            var source = (ReviveSource)st.ReviveRequestedSource;
            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "FinishReviveBegin", "| applying post-revive effects");
            }

            PostRevivalController.BeginPostRevival(player, playerId, st, applyFinalize);
            ReviveDebug.Log("FinishRevive_PostRevivalStarted", playerId, player.IsYourPlayer, $"source={source}");

            st.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            DownedStateController.ClearTimers(st);
            ReviveDebug.Log("FinishRevive_Complete", playerId, player.IsYourPlayer, null);

            if (player.IsYourPlayer)
            {
                PlayerMessageRouter.Notify(Color.green, msg, MessageAudience.LocalPlayer);
            }
            Plugin.LogSource.LogInfo($"[Downed] revive complete for {playerId}");

            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "FinishReviveEnd", "| revive complete UI shown");
            }
        }
    }
}
