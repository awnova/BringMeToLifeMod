using System;
using EFT;
using KeepMeAlive.Components;
using KeepMeAlive.Fika;
using KeepMeAlive.Helpers;
using UnityEngine;

namespace KeepMeAlive.Features
{
    /// <summary>
    /// Manages the post-revival lifecycle: invulnerability period, cooldown timer, and cleanup when those phases end.
    /// </summary>
    internal static class PostRevivalController
    {
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
                st.State = RMState.None;
                st.CooldownTimer = 0f;
                if (player.IsYourPlayer) VFX_UI.Text(Color.green, "Revival cooldown ended - you can now be revived");
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
                    VFX_UI.HideTransitPanel();
                    st.CriticalStateMainTimer?.Stop(); st.CriticalStateMainTimer = null;
                    VFX_UI.ObjectivePanel(Color.blue, VFX_UI.Position.BottomCenter, "Invulnerable {0:F1}", PostReviveEffects.GetInvulnDuration(source));
                    PlayerRestorations.RestorePlayerWeapon(player);
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
                PlayerRestorations.RestorePlayerMovement(player);
                st.OriginalMovementSpeed = -1f;
            }

            st.State = RMState.CoolDown;
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

            try { MedicalAnimations.CleanupFakeItems(player, source == ReviveSource.Self ? MedicalAnimations.SurgicalItemType.SurvKit : MedicalAnimations.SurgicalItemType.CMS); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[PostRevival] CleanupFakeItems error: {ex.Message}"); }
        }
    }
}
