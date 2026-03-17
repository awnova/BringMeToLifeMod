//====================[ Imports ]====================
using System;
using EFT;
using KeepMeAlive.Components;
using KeepMeAlive.Fika;
using KeepMeAlive.Helpers;
using UnityEngine;

namespace KeepMeAlive.Features
{
    //====================[ PostRevivalController ]====================
    internal static class PostRevivalController
    {
        //====================[ Public Tick ]====================
        public static void TickInvulnerability(Player player)
        {
            var st = RMSession.GetPlayerState(player.ProfileId);
            if (st.State != RMState.Revived) return;

            if (st.InvulnerabilityTimer <= 0f)
            {
                EndInvulnerability(player);
                return;
            }

            st.RevivePromptTimer?.Update();
            st.InvulnerabilityTimer -= Time.deltaTime;

            if (st.InvulnerabilityTimer <= 0f) EndInvulnerability(player);
        }

        public static void TickCooldown(Player player)
        {
            var st = RMSession.GetPlayerState(player.ProfileId);
            if (st.State != RMState.CoolDown || st.CooldownTimer <= 0f) return;

            st.CooldownTimer -= Time.deltaTime;

            if (st.CooldownTimer <= 0f)
            {
                RMSession.SetPlayerState(player.ProfileId, RMState.None);
                st.CooldownTimer = 0f;
                if (player.IsYourPlayer)
                {
                    DownedUiBlocker.SetBlocked(false);
                    VFX_UI.Text(Color.green, "Revival cooldown ended - you can now be revived");
                }
            }
        }

        //====================[ Revival Flow ]====================
        public static void BeginPostRevival(Player player, string playerId, RMPlayer st, bool applyFinalize)
        {
            RMSession.SetPlayerState(playerId, RMState.Revived);
            st.KillOverride = false;
            RMSession.RemovePlayerFromCriticalPlayers(playerId);

            st.IsReviveProgressActive = false;
            st.IsBeingRevived = false;
            st.IsSelfReviving = false;
            st.CurrentReviverId = string.Empty;

            if (player != null)
            {
                RevivalController.StopSilentInventoryReviveAnimation(player, st, "BeginPostRevival");
                GodMode.ForceEnable(player);
                try { GhostMode.ExitGhostMode(player); } catch { }

                if (player.IsYourPlayer)
                {
                    // Restore moment: unblock UI and return weapon immediately for both Self and Team revive.
                    DownedUiBlocker.SetBlocked(false);
                    PlayerRestorations.RestorePlayerWeapon(player);
                }

                if (player.IsYourPlayer && applyFinalize)
                {
                    try { PostReviveEffects.Apply(player, (ReviveSource)st.ReviveRequestedSource); }
                    catch (Exception ex) { Plugin.LogSource.LogError($"[PostRevival] PostReviveEffects error: {ex.Message}"); }
                }

                StartInvulnerabilityPeriod(player, st);
            }
            else
            {
                var source = (ReviveSource)st.ReviveRequestedSource;
                st.InvulnerabilityTimer = PostReviveEffects.GetInvulnDuration(source);
            }
        }

        public static void StartInvulnerabilityPeriod(Player player, RMPlayer st)
        {
            try
            {
                var source = (ReviveSource)st.ReviveRequestedSource;
                st.InvulnerabilityTimer = PostReviveEffects.GetInvulnDuration(source);

                if (st.OriginalMovementSpeed > 0)
                {
                    float invulnSpeed = st.OriginalMovementSpeed * PostReviveEffects.GetInvulnSpeedMultiplier(source);
                    player.Physical.WalkSpeedLimit = invulnSpeed;
                }

                if (player.MovementContext != null)
                {
                    var mc = player.MovementContext;
                    mc.IsInPronePose = false;
                    mc.SetPoseLevel(1f, true);
                    mc.EnableSprint(true);
                    DownedMovementController.ReattachMovementHooks(player);
                }

                if (player.IsYourPlayer)
                {
                    DownedUiBlocker.SetBlocked(false);
                    VFX_UI.HideTransitPanel();
                    st.CriticalStateMainTimer?.Stop(); st.CriticalStateMainTimer = null;
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, "Invulnerable {0:F1}", PostReviveEffects.GetInvulnDuration(source));
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogError($"[PostRevival] StartInvulnerabilityPeriod error: {ex.Message}"); }
        }

        public static void EndInvulnerability(Player player)
        {
            var st = RMSession.GetPlayerState(player.ProfileId);
            var source = (ReviveSource)st.ReviveRequestedSource;
            st.CurrentReviverId = string.Empty;

            GodMode.Disable(player);
            DownedHealthAndEffectsManager.RemoveRevivableState(player);

            if (player.IsYourPlayer)
            {
                PlayerRestorations.RestorePlayerMovement(player, forceStandingPose: false);
                st.OriginalMovementSpeed = -1f;
            }

            RMSession.SetPlayerState(player.ProfileId, RMState.CoolDown);
            float cd = PostReviveEffects.GetCooldownDuration(source);
            st.CooldownTimer = cd;
            st.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (player.IsYourPlayer)
            {
                RevivalAuthority.NotifyEndInvulnerability(player.ProfileId, cd);
                FikaBridge.SendPlayerStateResetPacket(player.ProfileId, isDead: false, cd);
                st.ResyncCooldown = -1f;
                VFX_UI.HideObjectivePanel();
                VFX_UI.Text(Color.cyan, $"Invulnerability ended. Revival cooldown: {cd:F0}s");
                PostReviveEffects.ApplyCooldownEffect(player, cd);
            }

        }
    }
}
