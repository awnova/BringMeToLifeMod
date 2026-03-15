//====================[ Imports ]====================
using System;
using System.Collections;
using EFT;
using UnityEngine;
using KeepMeAlive.Helpers;
using KeepMeAlive.Components;
using KeepMeAlive.Fika;

namespace KeepMeAlive.Features
{
    //====================[ DownedStateController ]====================
    // Slim state-machine coordinator. Domain logic lives in:
    //   BodyInteractableManager, DownedHealthAndEffectsManager,
    //   DownedMovementController, PostRevivalController, RevivalController
    internal static class DownedStateController
    {

        //====================[ Shared Helpers ]====================
        internal static IEnumerator DelayedActionAfterSeconds(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            try { action?.Invoke(); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] delayed action error: {ex.Message}"); }
        }

        internal static void ClearTimers(RMPlayer st)
        {
            st.CriticalStateMainTimer?.Stop(); st.CriticalStateMainTimer = null;
            st.RevivePromptTimer?.Stop(); st.RevivePromptTimer = null;
        }

        internal static void ClearRevivePromptTimer(RMPlayer st)
        {
            st.RevivePromptTimer?.Stop();
            st.RevivePromptTimer = null;
        }

        private static bool CanUseSelfRevive(Player player)
        {
            if (!RevivalModSettings.SELF_REVIVAL_ENABLED.Value) return false;
            if (RevivalModSettings.NO_DEFIB_REQUIRED.Value) return true;
            return Utils.HasDefib(player);
        }

        internal static void ShowSelfRevivePromptIfEligible(Player player)
        {
            if (player == null || !player.IsYourPlayer) return;
            if (!CanUseSelfRevive(player)) return;

            KeyCode key = RevivalModSettings.SELF_REVIVAL_KEY.Value;
            VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, $"Revive! [{key}]");
        }

        private static void HideAllPanelsAndStop(RMPlayer st)
        {
            VFX_UI.HideTransitPanel();
            VFX_UI.HideObjectivePanel();
            ClearTimers(st);
        }

        internal static void CancelReviveState(Player player, RMPlayer st, string message = null, Color? messageColor = null)
        {
            if (st.State == RMState.Reviving)
            {
                Plugin.LogSource.LogInfo($"[SelfReviveTrace] CancelReviveState ignored while already Reviving for {player?.ProfileId}");
                return;
            }

            st.IsBeingRevived = false;
            st.IsSelfReviving = false;
            st.IsPlayingRevivalAnimation = false;
            st.SelfReviveHoldTime = 0f;
            st.SelfReviveCommitted = false;
            st.SelfReviveAuthPending = false;
            VFX_UI.HideObjectivePanel();
            ClearRevivePromptTimer(st);
            ShowSelfRevivePromptIfEligible(player);

            if (!string.IsNullOrEmpty(message))
            {
                VFX_UI.Text(messageColor ?? Color.yellow, message);
            }
        }

        private static void BeginNewReviveCycle(RMPlayer st)
        {
            st.ReviveCycleId++;
            st.FinalizedReviveCycleId = -1;
        }

        internal static bool TryCommitReviveFinalizeForCycle(string source, string playerId, RMPlayer st)
        {
            if (st.IsReviveFinalizeCommittedForCurrentCycle)
            {
                Plugin.LogSource.LogInfo($"[ReviveFinalize] Suppressed duplicate finalize ({source}) for {playerId} in cycle {st.ReviveCycleId}");
                return false;
            }

            st.FinalizedReviveCycleId = st.ReviveCycleId;
            return true;
        }

private static void SafeCleanupFakeItems(Player player, MedicalAnimations.SurgicalItemType? consumedItem = null)
        {
            try { MedicalAnimations.CleanupFakeItems(player, consumedItem); }
            catch (Exception ex) { Plugin.LogSource.LogError($"CleanupFakeItems error: {ex.Message}"); }
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
                    BodyInteractableManager.Remove(id);
                    DeathMode.ForceBleedout(player);
                    return;
                }

                st.CooldownTimer = 0f;
                st.KillOverride = false;
                st.PlayerDamageType = damageType;
                st.State = RMState.BleedingOut;
                BeginNewReviveCycle(st);
                st.CriticalTimer = RevivalModSettings.CRITICAL_STATE_TIME.Value;
                st.ReviveRequestedSource = 0;
                st.IsPlayingRevivalAnimation = false;
                st.IsBeingRevived = false;
                st.IsSelfReviving = false;
                st.SelfRevivalKeyHoldDuration.Clear();
                st.SelfReviveHoldTime = 0f;
                st.SelfReviveCommitted = false;
                st.SelfReviveAuthPending = false;
                st.SelfReviveAttemptId = 0;
                st.CurrentReviverId = string.Empty;

                BodyInteractableManager.ForceClosePicker(id);
                RMSession.AddToCriticalPlayers(id);
                DownedHealthAndEffectsManager.RestoreVitalsToMinimum(player);

                if (RevivalModSettings.GHOST_MODE.Value) GhostMode.EnterGhostModeById(id);
                if (RevivalModSettings.GOD_MODE.Value) GodMode.Enable(player);

                if (player.IsYourPlayer)
                {
                    FikaBridge.SendBleedingOutPacket(id, st.CriticalTimer);
                    RevivalAuthority.NotifyBeginCritical(id);
                    st.ResyncCooldown = -1f;

                    ReviveDebug.Log("EnterDowned_CreateFakeItems", id, player.IsYourPlayer, null);
                    bool cmsOk = false, survOk = false;
                    try { var cms = MedicalAnimations.CreateInQuestInventory(player, MedicalAnimations.SurgicalItemType.CMS); cmsOk = cms != null; } catch (Exception ex) { Plugin.LogSource.LogError($"CMS error: {ex.Message}"); }
                    try { var surv = MedicalAnimations.CreateInQuestInventory(player, MedicalAnimations.SurgicalItemType.SurvKit); survOk = surv != null; } catch (Exception ex) { Plugin.LogSource.LogError($"SurvKit error: {ex.Message}"); }
                    ReviveDebug.Log("EnterDowned_CreateFakeItems_Result", id, player.IsYourPlayer, $"CMS={cmsOk} SurvKit={survOk}");

                    DownedHealthAndEffectsManager.ApplyCriticalEffects(player);
                    DownedMovementController.ApplyRevivableState(player);
                    ShowCriticalStateUI(player, st);
                }

                Plugin.LogSource.LogInfo($"[Downed] Player {id} entered critical state (local={player.IsYourPlayer})");
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] EnterDowned error: {ex.Message}"); }
        }

        public static void ExitDowned(Player player)
        {
            if (player == null) return;

            var st = RMSession.GetPlayerState(player.ProfileId);

            if (st.ReviveAnimationCoroutine != null)
            {
                Plugin.StaticCoroutineRunner.StopCoroutine(st.ReviveAnimationCoroutine);
                st.ReviveAnimationCoroutine = null;
            }

            st.State = RMState.None;
            st.IsPlayingRevivalAnimation = false;
            st.IsBeingRevived = false;
            st.IsSelfReviving = false;
            st.SelfRevivalKeyHoldDuration.Clear();
            st.SelfReviveHoldTime = 0f;
            st.SelfReviveCommitted = false;
            st.SelfReviveAuthPending = false;
            st.CurrentReviverId = string.Empty;
            st.ReviveRequestedSource = 0;
            st.FinalizedReviveCycleId = -1;
            HideAllPanelsAndStop(st);

            st.InvulnerabilityTimer = 0f;
            GodMode.Disable(player);
            DownedHealthAndEffectsManager.RemoveRevivableState(player);
            PlayerRestorations.RestorePlayerMovement(player);
            st.OriginalMovementSpeed = -1f;
            SafeCleanupFakeItems(player);
            PlayerRestorations.RestorePlayerWeapon(player);

            BodyInteractableManager.Remove(player.ProfileId);
        }

        //====================[ Per-Frame Ticks ]====================
        public static void TickDowned(Player player)
        {
            var st = RMSession.GetPlayerState(player.ProfileId);
            if (!st.IsCritical) return;

            ReviveDebug.Log("TickDowned_Enter", player.ProfileId, player.IsYourPlayer, $"state={st.State}");
            PlayerRestorations.StoreOriginalMovementSpeed(player);
            DownedMovementController.ApplyDownedMovementSpeed(player, st);
            DownedMovementController.ApplyDownedMovementRestrictions(player, st);

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

            RevivalController.TickSelfRevival(player, st);
            RevivalController.ObserveRevivingState(player, st);

            if (st.IsBeingRevived && st.State == RMState.BleedingOut && !string.IsNullOrEmpty(st.CurrentReviverId))
            {
                st.BeingRevivedWatchdogTimer -= Time.deltaTime;
                if (st.BeingRevivedWatchdogTimer <= 0f)
                {
                    Plugin.LogSource.LogWarning($"[Downed] Reviver watchdog expired for {player.ProfileId}; clearing IsBeingRevived");
                    st.CurrentReviverId = string.Empty;
                    CancelReviveState(player, st, "Reviver disconnected or timed out.", Color.yellow);
                }
            }

            if (st.State == RMState.BleedingOut && !st.IsBeingRevived && !st.IsSelfReviving && (st.CriticalTimer <= 0f || Input.GetKeyDown(RevivalModSettings.GIVE_UP_KEY.Value)))
            {
                ClearTimers(st);
                BodyInteractableManager.Remove(player.ProfileId);
                DeathMode.ForceBleedout(player);
            }
        }

        public static void TickResync(Player player)
        {
            if (!player.IsYourPlayer) return;

            var st = RMSession.GetPlayerState(player.ProfileId);
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
                ShowSelfRevivePromptIfEligible(player);
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[DownedStateController] ShowCriticalStateUI error: {ex.Message}"); }
        }

        private static void TryLazyShowTransitTimer(Player player, RMPlayer st)
        {
            if (!player.IsYourPlayer) return;
            if (st.CriticalTimer <= 0.5f) return;
            st.CriticalStateMainTimer = VFX_UI.TransitPanel(VFX_UI.Gradient(Color.red, Color.black), VFX_UI.Position.MiddleCenter, "BLEEDING OUT", st.CriticalTimer);
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