using EFT;
using EFT.HealthSystem;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using UnityEngine;
using EFT.Communications;
using Comfort.Common;
using RevivalMod.Helpers;
using RevivalMod.Fika;
using RevivalMod.Components;
using RevivalMod.Patches;

namespace RevivalMod.Features
{
    /// <summary>
    /// Implements a second-chance mechanic for players, allowing them to enter a critical state
    /// instead of dying, and use a defibrillator to revive.
    /// </summary>
    internal class RevivalFeatures : ModulePatch
    {
        #region Constants

        // Player movement and behavior constants
        // Note: Movement speed is now configurable via RevivalModSettings.DOWNED_MOVEMENT_SPEED
        private const bool FORCE_CROUCH_DURING_INVULNERABILITY = true;
        private const bool DISABLE_SHOOTING_DURING_INVULNERABILITY = true;

        #endregion

        // Simple coroutine helper to run an action after a delay
        private static IEnumerator DelayedActionAfterSeconds(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in delayed action: {ex.Message}");
            }
        }

        #region State Tracking

        // DEPRECATED: Use RMSession.GetPlayerState(playerId) instead
        // Kept for backward compatibility during migration
        [Obsolete("Use RMSession.GetPlayerState() instead")]
        public static Dictionary<string, RMPlayer> _playerList => RMSession.GetPlayerStates();

        // Reference to local player
        private static Player PlayerClient { get; set; }

        #endregion

        #region Core Patch Implementation

        protected override MethodBase GetTargetMethod()
        {
            // Patch the Update method of Player to check for revival and manage states
            return AccessTools.Method(typeof(Player), nameof(Player.UpdateTick));
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            try
            {
                string playerId = __instance.ProfileId;
                PlayerClient = __instance;

                // Only process for the local player
                if (!PlayerClient.IsYourPlayer)
                    return;

                // Test keybinds for surgical animations (F3 = SurvKit, F4 = CMS)
                CheckTestKeybinds(__instance);

                // Only process revival states if player is in the player list (critical state)
                if (!RMSession.HasPlayerState(playerId))
                    return;

                // Process player states
                ProcessInvulnerabilityState(PlayerClient, playerId);
                ProcessCriticalState(PlayerClient, playerId);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in RevivalFeatures patch: {ex.Message}");
            }
        }

        #endregion

        #region State Processing Methods

        /// <summary>
        /// Processes invulnerability state and timer for a player
        /// </summary>
        private static void ProcessInvulnerabilityState(Player player, string playerId)
        {
            var playerState = RMSession.GetPlayerState(playerId);
            if (!playerState.IsInvulnerable)
                return;

            float timer = playerState.InvulnerabilityTimer;

            if (!(timer > 0)) 
                return;
            
            timer -= Time.deltaTime;
            playerState.InvulnerabilityTimer = timer;

            // Apply invulnerability restrictions
            ApplyInvulnerabilityRestrictions(player);

            // End invulnerability if timer is up
            if (timer <= 0)
                EndInvulnerability(player);
        }

        /// <summary>
        /// Applies movement and action restrictions during invulnerability period
        /// </summary>
        private static void ApplyInvulnerabilityRestrictions(Player player)
        {
            // Force player to crouch during invulnerability if enabled
            if (FORCE_CROUCH_DURING_INVULNERABILITY && 
                player.MovementContext.PoseLevel > 0)
                player.MovementContext.SetPoseLevel(0);

            // Disable shooting during invulnerability if enabled
            if (DISABLE_SHOOTING_DURING_INVULNERABILITY && 
                player.HandsController.IsAiming)
                player.HandsController.IsAiming = false;
        }

        /// <summary>
        /// Processes critical state and checks for revival inputs
        /// </summary>
        private static void ProcessCriticalState(Player player, string playerId)
        {
            // Check if player is in critical state
            var playerState = RMSession.GetPlayerState(playerId);
            if (!playerState.IsCritical)
                return;

            // Store original movement speed if not already stored
            if (playerState.OriginalMovementSpeed < 0)
                playerState.OriginalMovementSpeed = player.Physical.WalkSpeedLimit;

            // Freeze movement if being revived (teammate hold OR actual revival animation)
            if (playerState.IsBeingRevived || playerState.IsPlayingRevivalAnimation)
            {
                player.Physical.WalkSpeedLimit = 0f;
            }
            else
            {
                // Severely restrict movement (convert percentage to decimal multiplier)
                player.Physical.WalkSpeedLimit = playerState.OriginalMovementSpeed * (RevivalModSettings.DOWNED_MOVEMENT_SPEED.Value / 100f);
            }
            
            // Force player to crouch
            if (player.MovementContext != null)
            {
                player.HandsController.IsAiming = false;
                player.MovementContext.SetPoseLevel(0f, true);
                player.MovementContext.IsInPronePose = true;
                player.ActiveHealthController.SetStaminaCoeff(1f);
                
                // Only force empty hands if NOT playing a revival animation
                if (!playerState.IsPlayingRevivalAnimation)
                {
                    player.SetEmptyHands(null);
                }
            }
            else
            {
                Plugin.LogSource.LogError("player.MovementContext is null!");
            }

            // Update the main critical state timer
            playerState.CriticalStateMainTimer?.Update();
            
            // Update the revive prompt timer
            playerState.RevivePromptTimer?.Update();
            
            // Sync the internal timer with the CustomTimer
            if (playerState.CriticalStateMainTimer != null && playerState.CriticalStateMainTimer.IsRunning)
            {
                TimeSpan remaining = playerState.CriticalStateMainTimer.GetTimeSpan();
                playerState.CriticalTimer = (float)remaining.TotalSeconds;
            }

            // Allow self-revival checks to run before the expiry/give-up check so a held key that completes
            // exactly when the critical timer hits zero can still trigger the revival flow instead of immediate death.
            CheckForSelfRevival(player);

            // Check for give up key or timer runs out
            if (playerState.CriticalTimer <= 0 ||
                Input.GetKeyDown(RevivalModSettings.GIVE_UP_KEY.Value))
            {
                playerState.CriticalStateMainTimer?.StopTimer();
                playerState.CriticalStateMainTimer = null;
                playerState.RevivePromptTimer?.StopTimer();
                playerState.RevivePromptTimer = null;

                ForcePlayerDeath(player);
                
                return;
            }
        }

        /// <summary>
        /// Checks for test keybinds to trigger surgical animations (only active when TESTING mode is enabled)
        /// </summary>
        private static void CheckTestKeybinds(Player player)
        {
            // Only enable test keybinds when TESTING mode is active
            if (!RevivalModSettings.TESTING.Value)
                return;

            try
            {
                // F3 = SurvKit animation
                if (Input.GetKeyDown(KeyCode.F3))
                {
                    MedicalAnimations.PlaySurgicalAnimation(player, MedicalAnimations.SurgicalItemType.SurvKit);
                }

                // F4 = CMS animation
                if (Input.GetKeyDown(KeyCode.F4))
                {
                    MedicalAnimations.PlaySurgicalAnimation(player, MedicalAnimations.SurgicalItemType.CMS);
                }

                // F7 = remove local player from AI enemy lists (enter ghost mode) - test
                if (Input.GetKeyDown(KeyCode.F7))
                {
                    try
                    {
                        if (player != null && player.IsYourPlayer)
                        {
                            // Use id-based helper so local testing and networked behavior share the same path
                            GhostModeEnemyManager.EnterGhostModeById(player.ProfileId);
                            Plugin.LogSource.LogInfo("GhostMode test: F7 pressed - EnterGhostMode called");
                            NotificationManagerClass.DisplayMessageNotification(
                                "GhostMode: Entered ghost mode (F7)",
                                ENotificationDurationType.Default,
                                ENotificationIconType.Default,
                                Color.cyan);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogSource.LogError($"GhostMode test F7 error: {ex.Message}");
                        NotificationManagerClass.DisplayMessageNotification(
                            $"GhostMode F7 error: {ex.Message}",
                            ENotificationDurationType.Default,
                            ENotificationIconType.Alert,
                            Color.red);
                    }
                }

                // F8 = re-add local player to AI enemy lists (exit ghost mode) - test
                if (Input.GetKeyDown(KeyCode.F8))
                {
                    try
                    {
                        if (player != null && player.IsYourPlayer)
                        {
                            // Use id-based helper so local testing and networked behavior share the same path
                            GhostModeEnemyManager.ExitGhostModeById(player.ProfileId);
                            Plugin.LogSource.LogInfo("GhostMode test: F8 pressed - ExitGhostMode called");
                            NotificationManagerClass.DisplayMessageNotification(
                                "GhostMode: Exited ghost mode (F8)",
                                ENotificationDurationType.Default,
                                ENotificationIconType.Default,
                                Color.cyan);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogSource.LogError($"GhostMode test F8 error: {ex.Message}");
                        NotificationManagerClass.DisplayMessageNotification(
                            $"GhostMode F8 error: {ex.Message}",
                            ENotificationDurationType.Default,
                            ENotificationIconType.Alert,
                            Color.red);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[TestKeybinds] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if self-revival is possible and processes key input with hold-to-revive behavior
        /// </summary>
        private static void CheckForSelfRevival(Player player)
        {
            GamePlayerOwner owner = player.GetComponentInParent<GamePlayerOwner>();
            var playerState = RMSession.GetPlayerState(player.ProfileId);
            
            if (!RevivalModSettings.SELF_REVIVAL_ENABLED.Value) 
                return;
            
            KeyCode revivalKey = RevivalModSettings.SELF_REVIVAL_KEY.Value;

            // Start key hold tracking when key is first pressed
            if (Input.GetKeyDown(revivalKey))
            {
                // Check if the player has the revival item
                bool hasDefib = Utils.HasDefib(player);
                
                if (!hasDefib)
                {
                    Plugin.LogSource.LogInfo($"Player {player.ProfileId} has no defibrillator.");

                    VFX_UI.ShowCriticalNotification("No defibrillator found! Unable to revive!", ENotificationDurationType.Long);
                    
                    return;
                }
                
                playerState.SelfRevivalKeyHoldDuration[revivalKey] = 0f;
                
                // Set flag to freeze movement during the hold
                playerState.IsBeingRevived = true;

                playerState.CriticalStateMainTimer?.StopTimer();
                
                // Show hold prompt using ShowObjectivesPanel (TimerPanel) for the 2-second hold
                const float initialHold = 2f;
                owner.ShowObjectivesPanel("Reviving {0:F1}", initialHold);
                
                // Color the objectives panel blue for revival operation
                VFX_UI.ColorObjectivesPanelBlue();
            }

            // Update hold duration while key is held
            if (Input.GetKey(revivalKey) && 
                playerState.SelfRevivalKeyHoldDuration.ContainsKey(revivalKey))
            {
                playerState.SelfRevivalKeyHoldDuration[revivalKey] += Time.deltaTime;

                float holdDuration = playerState.SelfRevivalKeyHoldDuration[revivalKey];
                const float requiredDuration = 2f;

                // Trigger revival when key is held long enough
                if (holdDuration >= requiredDuration)
                {
                    playerState.SelfRevivalKeyHoldDuration.Remove(revivalKey);

                    // Stop timers
                    playerState.CriticalStateMainTimer?.StopTimer();
                    
                    // Stop forcing empty hands to allow the animation to play (movement freeze handled by update loop)
                    playerState.IsPlayingRevivalAnimation = true;

                    // Start the surgical animation (SurvKit) and perform the actual revival when animation completes
                        // Start the SurvKit animation for visuals and sync it to the UI revive countdown (so both finish together).
                        // We intentionally ignore the return value so a broken animation won't prevent revival.
                        float revivalDuration = RevivalModSettings.SELF_REVIVE_ANIMATION_DURATION.Value;
                        
                        _ = MedicalAnimations.PlaySurgicalAnimationForDuration(player, MedicalAnimations.SurgicalItemType.SurvKit, revivalDuration);
                        // Use the configured revival duration for the countdown timer with red-to-green transition
                        playerState.CriticalStateMainTimer = new CustomTimer();
                        playerState.CriticalStateMainTimer.StartCountdown(revivalDuration, "Reviving", TimerPosition.MiddleCenter, TimerColorMode.RedToGreen);

                        // Schedule the actual revival when countdown finishes via a coroutine
                        Plugin.StaticCoroutineRunner.StartCoroutine(DelayedActionAfterSeconds(revivalDuration, () =>
                        {
                            TryPerformManualRevival(player);

                            if (!RevivalModSettings.TESTING.Value)
                            {
                                Utils.ConsumeDefibItem(player, Utils.GetDefib(player));
                            }
                        }));
                }
            }

            // Reset when key is released
            if (!Input.GetKeyUp(revivalKey) ||
                !playerState.SelfRevivalKeyHoldDuration.ContainsKey(revivalKey)) 
                return;
            
            // Clear flags if cancelling early (crawl speed will be restored by update loop)
            playerState.IsBeingRevived = false;
            playerState.IsPlayingRevivalAnimation = false;
            
            // Close hold prompt
            owner.CloseObjectivesPanel();
            
            // Resume critical state timer with red-to-black color transition
            playerState.CriticalStateMainTimer?.StartCountdown(playerState.CriticalTimer,"Bleeding Out", TimerPosition.MiddleCenter, TimerColorMode.RedToBlack);

            VFX_UI.ShowCriticalNotification("Defibrillator use canceled", ENotificationDurationType.Default);

            playerState.SelfRevivalKeyHoldDuration.Remove(revivalKey);
        }



        #endregion

        #region Public API Methods

        /// <summary>
        /// Checks if a player is currently in critical state
        /// </summary>
        public static bool IsPlayerInCriticalState(string playerId)
        {
            // Use RMSession as single source of truth
            return RMSession.HasPlayerState(playerId) && 
                   RMSession.GetPlayerState(playerId).IsCritical;
        }

        /// <summary>
        /// Checks if a player is currently invulnerable
        /// </summary>
        public static bool IsPlayerInvulnerable(string playerId)
        {
            // Use RMSession as single source of truth
            return RMSession.HasPlayerState(playerId) && 
                   RMSession.GetPlayerState(playerId).IsInvulnerable;
        }

        private static void RemovePlayerFromCriticalState(string playerId)
        {
            RMSession.RemovePlayerFromCriticalPlayers(playerId);
            FikaBridge.SendRemovePlayerFromCriticalPlayersListPacket(playerId);
        }

        /// <summary>
        /// Sets a player's critical state status
        /// </summary>
        public static void SetPlayerCriticalState(Player player, bool criticalState, EDamageType damageType)
        {
            // Null guard player
            if (player is null)
                return;

            string playerId = player.ProfileId;

            // Keeps track of critical state and damage type in RMSession (single source of truth)
            var playerState = RMSession.GetPlayerState(playerId);
            playerState.IsCritical = criticalState;
            playerState.PlayerDamageType = damageType;

            if (criticalState)
                InitializeCriticalState(player, playerId);
            else
                CleanupCriticalState(player, playerId);
        }

        /// <summary>
        /// Starts the revival animation on the revivee (called when packet received)
        /// </summary>
        public static bool TryPerformRevivalByTeammate(string playerId)
        {
            if (playerId != Singleton<GameWorld>.Instance.MainPlayer.ProfileId)
                return false;

            Player player = Singleton<GameWorld>.Instance.MainPlayer;

            try
            {
                // Stop forcing empty hands to allow animation to play
                var playerState = RMSession.GetPlayerState(playerId);
                playerState.IsPlayingRevivalAnimation = true;
                
                // Movement is already frozen (speed set to 0 from ReviveStarted packet)

                // Play CMS animation scaled to match the configured teammate revive duration
                float reviveAnimDuration = RevivalModSettings.TEAMMATE_REVIVE_ANIMATION_DURATION.Value;
                bool animationStarted = MedicalAnimations.PlaySurgicalAnimationForDuration(
                    player, 
                    MedicalAnimations.SurgicalItemType.CMS,
                    reviveAnimDuration, // Use configured duration
                    () => CompleteTeammateRevival(playerId) // Callback when animation finishes
                );

                if (!animationStarted)
                {
                    Plugin.LogSource.LogError("Failed to start CMS animation for teammate revival");
                    return false;
                }

                Plugin.LogSource.LogInfo($"Starting CMS animation for player {playerId}, duration: {reviveAnimDuration}s");
                
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error starting teammate revival animation: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Completes the revival after animation finishes
        /// </summary>
        private static void CompleteTeammateRevival(string playerId)
        {
            try
            {
                Player player = Singleton<GameWorld>.Instance.MainPlayer;
                var playerState = RMSession.GetPlayerState(playerId);

                // Apply revival effects
                ApplyRevivalEffects(player);

                // Apply invulnerability
                StartInvulnerability(player);

                // Reset critical state and kill override
                playerState.IsCritical = false;
                playerState.IsPlayingRevivalAnimation = false;
                playerState.IsBeingRevived = false; // Clear the being revived flag
                playerState.KillOverride = false; // Reset kill override on successful revival

                // Set last revival time
                playerState.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // Stop and clear the revival timer
                playerState.CriticalStateMainTimer?.StopTimer();
                playerState.CriticalStateMainTimer = null;
                playerState.RevivePromptTimer?.StopTimer();
                playerState.RevivePromptTimer = null;

                // Show successful revival notification
                VFX_UI.ShowSuccessNotification("Revived by teammate! You are temporarily invulnerable but limited in movement.");

                Plugin.LogSource.LogInfo($"Team revival completed for player {playerId}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error completing teammate revival: {ex}");
            }
        }

        #endregion

        #region Revival Implementation

        /// <summary>
        /// Initializes critical state for a player
        /// </summary>
        private static void InitializeCriticalState(Player player, string playerId)
        {
            // Set the critical state timer
            var playerState = RMSession.GetPlayerState(playerId);
            playerState.CriticalTimer = RevivalModSettings.CRITICAL_STATE_TIME.Value;
            playerState.IsInvulnerable = true;

            // Apply effects and make player revivable
            ApplyCriticalEffects(player);
            ApplyRevivableState(player);

            // Show UI notification for local player
            if (player.IsYourPlayer)
            {
                VFX_UI.ShowCriticalStateNotification(player);

                // Create a countdown timer for critical state with red-to-black transition
                playerState.CriticalStateMainTimer = new CustomTimer();
                playerState.CriticalStateMainTimer.StartCountdown(RevivalModSettings.CRITICAL_STATE_TIME.Value, "Bleeding Out", TimerPosition.MiddleCenter, TimerColorMode.RedToBlack);
                
                // Show persistent "Hold F to revive" prompt on objective panel (TimerPanel)
                if (RevivalModSettings.SELF_REVIVAL_ENABLED.Value && Utils.HasDefib(player))
                {
                    if (player.GetComponentInParent<GamePlayerOwner>() is GamePlayerOwner owner)
                    {
                        owner.ShowObjectivesPanel($"HOLD [{RevivalModSettings.SELF_REVIVAL_KEY.Value}] TO REVIVE", 999999f);
                    }
                }
            }

            RMSession.AddToCriticalPlayers(playerId);
            FikaBridge.SendPlayerCriticalStatePacket(playerId);
        }


        /// <summary>
        /// Cleans up critical state when exiting that state
        /// </summary>
        private static void CleanupCriticalState(Player player, string playerId)
        {
            var playerState = RMSession.GetPlayerState(playerId);
            
            // Stop the main critical state timer
            playerState.CriticalStateMainTimer?.StopTimer();
            playerState.CriticalStateMainTimer = null;
            playerState.RevivePromptTimer?.StopTimer();
            playerState.RevivePromptTimer = null;

            // If player is leaving critical state without revival, clean up
            if (!(playerState.InvulnerabilityTimer <= 0)) 
                return;
            
            RemoveRevivableState(player);
            RestorePlayerMovement(player);
        }

        /// <summary>
        /// Attempts to perform manual revival
        /// </summary>
        private static bool TryPerformManualRevival(Player player)
        {
            if (player is null)
                return false;
            
            string playerId = player.ProfileId;
            var playerState = RMSession.GetPlayerState(playerId);
            
            // Remove from critical players list for multiplayer sync
            RemovePlayerFromCriticalState(playerId);
            
            // Apply revival effects with limited healing
            ApplyRevivalEffects(player);

            // Apply invulnerability period
            StartInvulnerability(player);

            player.Say(EPhraseTrigger.OnMutter, false, 2f, ETagStatus.Combat, 100, true);

            // Reset critical state and kill override
            playerState.IsCritical = false;
            playerState.IsBeingRevived = false; // Clear the being revived flag
            playerState.KillOverride = false; // Reset kill override on successful revival

            // Set last revival time
            playerState.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            playerState.CriticalStateMainTimer?.StopTimer();
            playerState.CriticalStateMainTimer = null;

            playerState.RevivePromptTimer?.StopTimer();
            playerState.RevivePromptTimer = null;

            // Show successful revival notification
            VFX_UI.ShowSuccessNotification("Defibrillator used successfully! You are temporarily invulnerable but limited in movement.");

            Plugin.LogSource.LogInfo($"Manual revival performed for player {playerId}");
            
            return true;
        }

        /// <summary>
        /// Checks if revival is on cooldown and shows notification if needed
        /// </summary>
        public static bool IsRevivalOnCooldown(string playerId)
        {
            var playerState = RMSession.GetPlayerState(playerId);
            long lastRevivalTime = playerState.LastRevivalTimesByPlayer;
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool isOnCooldown = (currentTime - lastRevivalTime) < RevivalModSettings.REVIVAL_COOLDOWN.Value;
            
            // Only show notification if not in test mode or not on cooldown
            if (!isOnCooldown) 
                return false;
            
            int remainingCooldown = (int)(RevivalModSettings.REVIVAL_COOLDOWN.Value - (currentTime - lastRevivalTime));
                    
            VFX_UI.ShowWarningNotification($"Revival on cooldown! Available in {remainingCooldown} seconds");

            return true;
        }

        /// <summary>
        /// Revives a teammate by a player with a defibrillator
        /// </summary>
        public static bool PerformTeammateRevival(string targetPlayerId, Player player)
        {
            try
            {
                Plugin.LogSource.LogInfo($"Performing teammate revival for player {targetPlayerId}");
                
                // Only consume defib if not in testing mode AND config says to consume it
                if (!RevivalModSettings.TESTING.Value && RevivalModSettings.CONSUME_DEFIB_ON_TEAMMATE_REVIVE.Value)
                {
                    Utils.ConsumeDefibItem(player, Utils.GetDefib(player));
                }

                RemovePlayerFromCriticalState(targetPlayerId);
                
                FikaBridge.SendReviveMePacket(targetPlayerId, player.ProfileId);

                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in teammate revival: {ex}");
                return false;
            }
        }

        #endregion

        #region Player Status Effects

        /// <summary>
        /// Applies critical state effects to player
        /// </summary>
        private static void ApplyCriticalEffects(Player player)
        {
            try
            {
                string playerId = player.ProfileId;
                var playerState = RMSession.GetPlayerState(playerId);

                // Store original movement speed if not already stored
                if (playerState.OriginalMovementSpeed < 0)
                    playerState.OriginalMovementSpeed = player.Physical.WalkSpeedLimit;

                // Apply visual and movement effects for entire critical state duration
                if (RevivalModSettings.CONTUSION_EFFECT.Value)
                    player.ActiveHealthController.DoContusion(RevivalModSettings.CRITICAL_STATE_TIME.Value, 1f);
                
                if (RevivalModSettings.STUN_EFFECT.Value)
                    // Cap stun effect at 20 seconds, but allow shorter duration if critical state time is less than 20
                    player.ActiveHealthController.DoStun(Math.Min(RevivalModSettings.CRITICAL_STATE_TIME.Value, 20f), 1f);

                // Freeze movement if being revived (teammate hold OR actual revival animation)
                if (playerState.IsBeingRevived || playerState.IsPlayingRevivalAnimation)
                {
                    player.Physical.WalkSpeedLimit = 0f;
                }
                else
                {
                    // Severely restrict movement (convert percentage to decimal multiplier)
                    player.Physical.WalkSpeedLimit = playerState.OriginalMovementSpeed * (RevivalModSettings.DOWNED_MOVEMENT_SPEED.Value / 100f);
                }

                // Force player to crouch
                player.MovementContext?.SetPoseLevel(0f, true);

                Plugin.LogSource.LogDebug($"Applied critical effects to player {playerId}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error applying critical effects: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores player movement capabilities after critical/invulnerable state
        /// </summary>
        private static void RestorePlayerMovement(Player player)
        {
            try
            {
                string playerId = player.ProfileId;
                var playerState = RMSession.GetPlayerState(playerId);

                // Restore original movement speed if we stored it
                player.Physical.WalkSpeedLimit = playerState.OriginalMovementSpeed;

                Plugin.LogSource.LogDebug($"Player WalkSpeedLimit: {player.Physical.WalkSpeedLimit}");

                // Reset pose to standing
                player.MovementContext.SetPoseLevel(1f);

                Plugin.LogSource.LogDebug($"Restored movement for player {playerId}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error restoring player movement: {ex.Message}");
            }
        }

        /// <summary>
        /// Makes the player enter a revivable state where AI ignores them
        /// </summary>
        private static void ApplyRevivableState(Player player)
        {
            try
            {
                string playerId = player.ProfileId;
                var playerState = RMSession.GetPlayerState(playerId);

                // Store original awareness value only if not already stored
                if (!playerState.HasStoredAwareness)
                {
                    playerState.OriginalAwareness = player.Awareness;
                    playerState.HasStoredAwareness = true;
                }

                // Configure player for revivable state
                player.Awareness = 0f; 
                // NOTE: Commented out PlayDeathSound() - this broadcasts death to clients in Fika
                // player.PlayDeathSound();
                player.HandsController.IsAiming = false;
                player.MovementContext.EnableSprint(false);
                player.MovementContext.SetPoseLevel(0f, true);
                player.MovementContext.IsInPronePose = true;
                player.SetEmptyHands(null);

                // Enter ghost mode - remove player from AI enemy lists
                if (RevivalModSettings.GHOST_MODE.Value)
                    GhostModeEnemyManager.EnterGhostMode(player);
                
                // Enable God mode
                if (RevivalModSettings.GOD_MODE.Value)
                    player.ActiveHealthController.SetDamageCoeff(0);

                GClass3756.ReleaseBeginSample("Player.OnDead.SoundWork", "OnDead");
                
                if (player.ShouldVocalizeDeath(player.LastDamagedBodyPart))
                {
                    EPhraseTrigger trigger = player.LastDamageType.IsWeaponInduced() ? EPhraseTrigger.OnDeath : EPhraseTrigger.OnAgony;
                    
                    try
                    {
                        player.Speaker.Play(trigger, player.HealthStatus, true, null);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(ex.Message);
                    }
                }
                
                player.MovementContext.ReleaseDoorIfInteractingWithOne();
                player.MovementContext.OnStateChanged -= player.method_17;
                player.MovementContext.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;

                player.MovementContext.StationaryWeapon?.Unlock(player.ProfileId);
                
                if (player.MovementContext.StationaryWeapon is not null && 
                    player.MovementContext.StationaryWeapon.Item == player.HandsController.Item)
                {
                    player.MovementContext.StationaryWeapon.Show();
                    player.ReleaseHand();
                    
                    return;
                }

                Plugin.LogSource.LogDebug($"Applied revivable state to player {playerId}");
                Plugin.LogSource.LogDebug($"Revivable State Variables - Awareness: {player.Awareness}, IsAlive: {player.ActiveHealthController.IsAlive}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error applying revivable state: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes the revivable state from a player
        /// </summary>
        private static void RemoveRevivableState(Player player)
        {
            try
            {
                string playerId = player.ProfileId;
                var playerState = RMSession.GetPlayerState(playerId);

                // Only restore if we have stored awareness
                if (playerState.HasStoredAwareness)
                {
                    // Restore awareness and visibility
                    player.Awareness = playerState.OriginalAwareness;
                    playerState.HasStoredAwareness = false;
                    
                    // Exit ghost mode - restore player to AI enemy lists
                    if (RevivalModSettings.GHOST_MODE.Value)
                        GhostModeEnemyManager.ExitGhostMode(player);

                    Plugin.LogSource.LogInfo($"Removed revivable state from player {playerId}");
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error removing revivable state: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies revival effects to the player with limited healing
        /// </summary>
        private static void ApplyRevivalEffects(Player player)
        {
            try
            {
                ActiveHealthController healthController = player.ActiveHealthController;

                if (healthController is null)
                {
                    Plugin.LogSource.LogError("Could not get ActiveHealthController");
                    return;
                }

                // Exit ghost mode - restore player to AI enemy lists
                if (RevivalModSettings.GHOST_MODE.Value)
                    GhostModeEnemyManager.ExitGhostMode(player);
                
                // Disable God mode
                if (RevivalModSettings.GOD_MODE.Value)
                    healthController.SetDamageCoeff(1);

                // Restore destroyed body parts if setting enabled
                BodyPartRestoration.RestoreDestroyedBodyParts(player);

                // Note: Contusion and stun effects are NOT reapplied after revival
                // They are only applied once when entering critical state

                Plugin.LogSource.LogInfo("Applied revival effects to player");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error applying revival effects: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts invulnerability period for a player
        /// </summary>
        private static void StartInvulnerability(Player player)
        {
            if (player is null)
                return;

            string playerId = player.ProfileId;
            var playerState = RMSession.GetPlayerState(playerId);

            playerState.IsInvulnerable = true;
            playerState.InvulnerabilityTimer = RevivalModSettings.REVIVAL_DURATION.Value;

            // Start visual effect
            player.StartCoroutine(VFX_UI.FlashInvulnerabilityEffect(player));

            Plugin.LogSource.LogInfo($"Started invulnerability for player {playerId} for {RevivalModSettings.REVIVAL_DURATION.Value} seconds");
        }

        /// <summary>
        /// Ends invulnerability period for a player
        /// </summary>
        private static void EndInvulnerability(Player player)
        {
            if (player is null)
                return;

            string playerId = player.ProfileId;
            var playerState = RMSession.GetPlayerState(playerId);

            playerState.IsInvulnerable = false;
            
            // Remove stealth from player
            RemoveRevivableState(player);

            // Remove movement restrictions
            RestorePlayerMovement(player);

            // Show notification
            if (player.IsYourPlayer)
            {
                VFX_UI.ShowNotification("Temporary invulnerability has ended");
            }

            Plugin.LogSource.LogInfo($"Ended invulnerability for player {playerId}");
        }

        /// <summary>
        /// Forces player death when revival fails or time runs out
        /// </summary>
        private static void ForcePlayerDeath(Player player)
        {
            try
            {
                string playerId = player.ProfileId;
                var playerState = RMSession.GetPlayerState(playerId);

                // Add to override list first (before any other operations)
                playerState.KillOverride = true;

                // Clean up all state tracking for this player
                playerState.IsInvulnerable = false;
                playerState.IsCritical = false;

                // Remove player from critical players list for network sync                

                // Show notification about death
                VFX_UI.ShowCriticalNotification("You have died");

                // Get the damage type that initially caused critical state
                EDamageType damageType = playerState.PlayerDamageType;

                // Stop countdown timer
                playerState.CriticalStateMainTimer?.StopTimer();
                playerState.CriticalStateMainTimer = null;
                
                RemovePlayerFromCriticalState(playerId);
                
                // Exit ghost mode before actual death
                if (RevivalModSettings.GHOST_MODE.Value)
                    GhostModeEnemyManager.ExitGhostMode(player);
                
                // Set health to 0 to allow natural death
                var chestHealth = player.ActiveHealthController.Dictionary_0[EBodyPart.Chest].Health;
                player.ActiveHealthController.Dictionary_0[EBodyPart.Chest].Health = 
                    new HealthValue(0f, chestHealth.Maximum, 0f);
                
                // Now call Kill() which will proceed with normal death since health is 0
                player.ActiveHealthController.Kill(damageType);

                Plugin.LogSource.LogInfo($"Player {playerId} has died after critical state");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error forcing player death: {ex.Message}");
            }
        }

        #endregion
    }
}