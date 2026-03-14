using System;
using System.Collections;
using System.Threading.Tasks;
using EFT;
using KeepMeAlive.Components;
using KeepMeAlive.Fika;
using KeepMeAlive.Helpers;
using UnityEngine;

namespace KeepMeAlive.Features
{
    /// <summary>
    /// Handles the entire self-revival flow: key-hold input, server authorization, animation playback,
    /// revival completion, and post-revival handoff.
    /// Also handles the shared ObserveRevivingState tick used by both self and team revives.
    /// </summary>
    internal static class SelfRevivalController
    {
        private static int _selfReviveTraceSequence;

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
            bool anim = st?.IsPlayingRevivalAnimation ?? false;

            Plugin.LogSource.LogInfo(
                $"[SelfReviveTrace #{seq:000}] {id} step='{step}' state={state} source={source} reviver='{reviver}' beingRevived={beingRevived} selfReviving={selfReviving} anim={anim} {details ?? string.Empty}");
        }

        //====================[ Per-Frame Tick ]====================
        public static void TickSelfRevival(Player player, RMPlayer st)
        {
            if (!RevivalModSettings.SELF_REVIVAL_ENABLED.Value) return;
            if (st.State != RMState.BleedingOut) return;
            if (!string.IsNullOrEmpty(st.CurrentReviverId) && st.CurrentReviverId != player.ProfileId) return;

            KeyCode key = RevivalModSettings.SELF_REVIVAL_KEY.Value;
            const float holdDuration = 2f;

            if (Input.GetKeyDown(key))
            {
                TraceSelfRevive(player, st, "KeyDown", $"| key={key}");

                if (!RevivalModSettings.NO_DEFIB_REQUIRED.Value && !Utils.HasDefib(player))
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

                TraceSelfRevive(player, st, "HoldStarted", "| holdTarget=2.00s");

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
            bool allowed = true;
            string denyReason = string.Empty;
            bool canCleanupAttempt = false;

            TraceSelfRevive(player, st, "AuthRequestStart", "| sending TryAuthorizeReviveStart(self)");

            var task = Task.Run(() =>
            {
                allowed    = RevivalAuthority.TryAuthorizeReviveStart(pid, pid, "self", out var reason);
                denyReason = reason ?? string.Empty;
            });

            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted)
            {
                allowed = false;
                denyReason = "Revive authorization failed";
                Plugin.LogSource.LogWarning($"[SelfReviveAuth] Authorization task fault for {pid}: {task.Exception?.GetBaseException().Message}");
            }

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
                st.SelfRevivalKeyHoldDuration.Remove(RevivalModSettings.SELF_REVIVAL_KEY.Value);
                yield break;
            }

            if (allowed)
            {
                if (!RevivalModSettings.NO_DEFIB_REQUIRED.Value)
                {
                    TraceSelfRevive(player, st, "DefibCheck", "| NO_DEFIB_REQUIRED=false");
                    var defib = Utils.GetDefib(player);
                    if (defib == null)
                    {
                        st.SelfRevivalKeyHoldDuration.Remove(RevivalModSettings.SELF_REVIVAL_KEY.Value);
                        st.SelfReviveAuthPending = false;
                        st.SelfReviveCommitted = false;
                        st.SelfReviveHoldTime = 0f;
                        st.IsSelfReviving = false;
                        TraceSelfRevive(player, st, "DefibMissingAfterAuth", "| canceling self-revive");
                        DownedStateController.CancelReviveState(player, st, "Defibrillator missing. Self-revive canceled.", Color.red);
                        yield break;
                    }

                    TraceSelfRevive(player, st, "DefibApply", $"| itemId={defib.Id}");
                    if (!Utils.TryApplyItemLikeTeamHeal(player, defib, "SelfReviveDefib"))
                    {
                        Plugin.LogSource.LogWarning($"[SelfReviveDefib] ApplyItem did not consume defib for {player.ProfileId}");
                    }
                }

                st.State = RMState.Reviving;
                st.IsPlayingRevivalAnimation = false;
                if (st.ReviveAnimationCoroutine != null)
                {
                    Plugin.StaticCoroutineRunner.StopCoroutine(st.ReviveAnimationCoroutine);
                    st.ReviveAnimationCoroutine = null;
                }
                st.SelfReviveAuthPending = false;
                st.SelfReviveCommitted = false;
                st.SelfReviveHoldTime = 0f;
                st.IsSelfReviving = false;
                TraceSelfRevive(player, st, "StateSetReviving", "| sending SelfReviveStart packet");
                FikaBridge.SendSelfReviveStartPacket(pid);
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
                st.SelfRevivalKeyHoldDuration.Remove(RevivalModSettings.SELF_REVIVAL_KEY.Value);
            }
            TraceSelfRevive(player, st, "AuthEnd", "| hold dictionary cleanup complete");
        }

        //====================[ Reviving State Observer ]====================
        public static void ObserveRevivingState(Player player, RMPlayer st)
        {
            ReviveDebug.Log("ObserveRevive_Enter", player.ProfileId, player.IsYourPlayer, $"state={st.State} anim={st.IsPlayingRevivalAnimation}");
            if (st.State != RMState.Reviving)
            {
                ReviveDebug.Log("ObserveRevive_SkipState", player.ProfileId, player.IsYourPlayer, $"state={st.State}");
                return;
            }

            if (st.IsPlayingRevivalAnimation)
            {
                if (st.ReviveAnimationCoroutine == null)
                {
                    ReviveDebug.Log("ObserveRevive_ResetStaleAnimFlag", player.ProfileId, player.IsYourPlayer, null);
                    st.IsPlayingRevivalAnimation = false;
                }
                else
                {
                    ReviveDebug.Log("ObserveRevive_SkipAlreadyPlaying", player.ProfileId, player.IsYourPlayer, null);
                    return;
                }
            }

            st.IsPlayingRevivalAnimation = true;
            var source = (ReviveSource)st.ReviveRequestedSource;

            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "ObserveRevivingState", "| entering self revive animation path");
            }

            float configuredDuration = source == ReviveSource.Team ? RevivalModSettings.TEAMMATE_REVIVE_ANIMATION_DURATION.Value : RevivalModSettings.SELF_REVIVE_ANIMATION_DURATION.Value;
            float duration = Mathf.Max(3f, configuredDuration);

            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "AnimationConfig", $"| duration={duration:F2}");
            }

            st.CriticalStateMainTimer?.Stop();
            st.CriticalStateMainTimer = VFX_UI.TransitPanel(VFX_UI.Gradient(Color.red, Color.green), VFX_UI.Position.MiddleCenter, "REVIVING", duration);

            VFX_UI.HideObjectivePanel();
            DownedStateController.ClearRevivePromptTimer(st);

            ReviveDebug.Log("ObserveRevive_BeforeApply", player.ProfileId, player.IsYourPlayer, $"duration={duration:F2} itemType={(source == ReviveSource.Self ? "SurvKit" : "CMS")}");
            bool started = MedicalAnimations.TryApplyWithDuration(player, source == ReviveSource.Self ? MedicalAnimations.SurgicalItemType.SurvKit : MedicalAnimations.SurgicalItemType.CMS, duration);
            ReviveDebug.Log("ObserveRevive_AfterApply", player.ProfileId, player.IsYourPlayer, $"started={started}");
            if (!started)
            {
                ReviveDebug.Log("ObserveRevive_ApplyFailedRetry", player.ProfileId, player.IsYourPlayer, null);
                st.IsPlayingRevivalAnimation = false;
                return;
            }
            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "AnimationStart", $"| started={started}");
            }

            int expectedCycle = st.ReviveCycleId;
            st.ReviveAnimationCoroutine = Plugin.StaticCoroutineRunner.StartCoroutine(DownedStateController.DelayedActionAfterSeconds(duration, () => OnRevivalAnimationComplete(player, expectedCycle)));
            ReviveDebug.Log("ObserveRevive_CoroutineStarted", player.ProfileId, player.IsYourPlayer, $"delay={duration:F2} cycle={expectedCycle}");
            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "AnimationCoroutineScheduled", $"| delay={duration:F2}");
            }
        }

        //====================[ Revival Completion ]====================
        private static void OnRevivalAnimationComplete(Player player, int expectedCycle)
        {
            if (player == null) return;
            var st = RMSession.GetPlayerState(player.ProfileId);
            ReviveDebug.Log("RevivalComplete_Enter", player.ProfileId, player.IsYourPlayer, $"state={st.State} cycle={st.ReviveCycleId} expected={expectedCycle}");
            if ((ReviveSource)st.ReviveRequestedSource == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "AnimationCompleteCallback", $"| callback fired (expected {expectedCycle}, current {st.ReviveCycleId})");
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
                    TraceSelfRevive(player, st, "AnimationCompleteIgnored", "| state is no longer Reviving");
                }
                return;
            }

            st.ReviveAnimationCoroutine = null;

            var msg = (ReviveSource)st.ReviveRequestedSource == ReviveSource.Self
                ? "Defibrillator used successfully! You are temporarily invulnerable."
                : "Revived by teammate! You are temporarily invulnerable.";

            string reviverId = (ReviveSource)st.ReviveRequestedSource == ReviveSource.Self ? string.Empty : (st.CurrentReviverId ?? string.Empty);
            ReviveDebug.Log("RevivalComplete_CallCompleteRevival", player.ProfileId, player.IsYourPlayer, $"reviverId={reviverId}");
            if ((ReviveSource)st.ReviveRequestedSource == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "AnimationCompleteProceed", "| calling CompleteRevival");
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

            st.State = RMState.Revived;
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
            FinishRevive(player, player.ProfileId, message);
        }

        private static void FinishRevive(Player player, string playerId, string msg)
        {
            var st = RMSession.GetPlayerState(playerId);
            if (!DownedStateController.TryCommitReviveFinalizeForCycle("LocalFinishRevive", playerId, st))
            {
                return;
            }

            var source = (ReviveSource)st.ReviveRequestedSource;
            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "FinishReviveBegin", "| applying post-revive effects");
            }

            try { GhostMode.ExitGhostMode(player); } catch { }
            try { PostReviveEffects.Apply(player, (ReviveSource)st.ReviveRequestedSource); } catch (Exception ex) { Plugin.LogSource.LogError($"PostReviveEffects error: {ex.Message}"); }

            st.IsPlayingRevivalAnimation = false;
            st.IsBeingRevived = false;
            st.IsSelfReviving = false;
            st.CurrentReviverId = string.Empty;
            st.KillOverride = false;
            PostRevivalController.StartInvulnerabilityPeriod(player, st);

            st.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            DownedStateController.ClearTimers(st);

            RMSession.RemovePlayerFromCriticalPlayers(playerId);

            VFX_UI.Text(Color.green, msg);
            Plugin.LogSource.LogInfo($"[Downed] revive complete for {playerId}");

            if (source == ReviveSource.Self)
            {
                TraceSelfRevive(player, st, "FinishReviveEnd", "| revive complete UI shown");
            }
        }
    }
}
