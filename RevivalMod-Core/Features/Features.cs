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
        private const float MOVEMENT_SPEED_MULTIPLIER = 0.1f;
        private const bool FORCE_CROUCH_DURING_INVULNERABILITY = true;
        private const bool DISABLE_SHOOTING_DURING_INVULNERABILITY = true;

        // Visual effect constants
        private const float FLASH_INTERVAL = 0.5f;

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

        // Key holding tracking
        private static readonly Dictionary<KeyCode, float> _selfRevivalKeyHoldDuration = [];

        // DEPRECATED: Use RMSession.GetPlayerState(playerId) instead
        // Kept for backward compatibility during migration
        [Obsolete("Use RMSession.GetPlayerState() instead")]
        public static Dictionary<string, RMPlayer> _playerList => RMSession.GetPlayerStates();

        // Reference to local player
        private static Player PlayerClient { get; set; }

        #endregion
        public static CustomTimer criticalStateMainTimer;

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

            // Severely restrict movement
            player.Physical.WalkSpeedLimit = MOVEMENT_SPEED_MULTIPLIER;
            
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
            criticalStateMainTimer?.Update();
            
            // Sync the internal timer with the CustomTimer
            if (criticalStateMainTimer != null && criticalStateMainTimer.IsRunning)
            {
                TimeSpan remaining = criticalStateMainTimer.GetTimeSpan();
                playerState.CriticalTimer = (float)remaining.TotalSeconds;
            }

            // Allow self-revival checks to run before the expiry/give-up check so a held key that completes
            // exactly when the critical timer hits zero can still trigger the revival flow instead of immediate death.
            CheckForSelfRevival(player);

            // Check for give up key or timer runs out
            if (playerState.CriticalTimer <= 0 ||
                Input.GetKeyDown(RevivalModSettings.GIVE_UP_KEY.Value))
            {
                criticalStateMainTimer?.StopTimer();
                criticalStateMainTimer = null;

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
            
            if (!RevivalModSettings.SELF_REVIVAL_ENABLED.Value) 
                return;
            
            KeyCode revivalKey = RevivalModSettings.SELF_REVIVAL_KEY.Value;

            // Start key hold tracking when key is first pressed
            if (Input.GetKeyDown(revivalKey))
            {
                // Check if the player has the revival item
                bool hasDefib = HasDefib(player);
                
                if (!hasDefib)
                {
                    Plugin.LogSource.LogInfo($"Player {player.ProfileId} has no defibrillator.");

                    NotificationManagerClass.DisplayMessageNotification(
                        "No defibrillator found! Unable to revive!",
                        ENotificationDurationType.Long,
                        ENotificationIconType.Alert,
                        Color.red);
                    
                    return;
                }
                
                _selfRevivalKeyHoldDuration[revivalKey] = 0f;

                criticalStateMainTimer?.StopTimer();
                
                // Show revive timer (labelled 'Reviving' when animation starts)
                const float initialHold = 2f;
                owner.ShowObjectivesPanel("Reviving {0:F1}", initialHold);
                
                // Try to color the objectives panel blue
                try
                {
                    var objectivesPanel = MonoBehaviourSingleton<GameUI>.Instance?.TimerPanel;
                    if (objectivesPanel != null)
                    {
                        RectTransform panel = objectivesPanel.transform.GetChild(0) as RectTransform;
                        if (panel != null)
                        {
                            var panelImage = panel.GetComponent<UnityEngine.UI.Image>();
                            if (panelImage != null)
                            {
                                panelImage.color = UnityEngine.Color.blue;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogDebug($"Could not color objectives panel: {ex.Message}");
                }
            }

            // Update hold duration while key is held
            if (Input.GetKey(revivalKey) && 
                _selfRevivalKeyHoldDuration.ContainsKey(revivalKey))
            {
                _selfRevivalKeyHoldDuration[revivalKey] += Time.deltaTime;

                float holdDuration = _selfRevivalKeyHoldDuration[revivalKey];
                const float requiredDuration = 2f;

                // Trigger revival when key is held long enough
                if (holdDuration >= requiredDuration)
                {
                    _selfRevivalKeyHoldDuration.Remove(revivalKey);

                    // Stop timers
                    criticalStateMainTimer?.StopTimer();
                    
                    // Stop forcing empty hands to allow the animation to play
                    RMSession.GetPlayerState(player.ProfileId).IsPlayingRevivalAnimation = true;

                    // Start the surgical animation (SurvKit) and perform the actual revival when animation completes
                        // Start the SurvKit animation for visuals and sync it to the UI revive countdown (so both finish together).
                        // We intentionally ignore the return value so a broken animation won't prevent revival.
                        float revivalDuration = RevivalModSettings.SELF_REVIVE_ANIMATION_DURATION.Value;
                        
                        _ = MedicalAnimations.PlaySurgicalAnimationForDuration(player, MedicalAnimations.SurgicalItemType.SurvKit, revivalDuration);
                        // Use the configured revival duration for the countdown timer with red-to-green transition
                        criticalStateMainTimer = new CustomTimer();
                        criticalStateMainTimer.StartCountdown(revivalDuration, "Reviving", TimerPosition.MiddleCenter, TimerColorMode.RedToGreen);

                        // Schedule the actual revival when countdown finishes via a coroutine
                        Plugin.StaticCoroutineRunner.StartCoroutine(DelayedActionAfterSeconds(revivalDuration, () =>
                        {
                            TryPerformManualRevival(player);

                            if (!RevivalModSettings.TESTING.Value)
                            {
                                ConsumeDefibItem(player, GetDefib(player));
                            }
                        }));
                }
            }

            // Reset when key is released
            if (!Input.GetKeyUp(revivalKey) ||
                !_selfRevivalKeyHoldDuration.ContainsKey(revivalKey)) 
                return;
            
            // Clear animation flag if cancelling early
            var playerState = RMSession.GetPlayerState(player.ProfileId);
            playerState.IsPlayingRevivalAnimation = false;
            
            // Close revive timer
            owner.CloseObjectivesPanel();
            
            // Resume critical state timer with red-to-black color transition
            criticalStateMainTimer?.StartCountdown(playerState.CriticalTimer,"Bleeding Out", TimerPosition.MiddleCenter, TimerColorMode.RedToBlack);

            NotificationManagerClass.DisplayMessageNotification(
                "Defibrillator use canceled",
                ENotificationDurationType.Default,
                ENotificationIconType.Default,
                Color.red);

            _selfRevivalKeyHoldDuration.Remove(revivalKey);
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
                RMSession.GetPlayerState(playerId).IsPlayingRevivalAnimation = true;

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

                // Apply revival effects
                ApplyRevivalEffects(player);

                // Apply invulnerability
                StartInvulnerability(player);

                // Reset critical state and kill override
                var playerState = RMSession.GetPlayerState(playerId);
                playerState.IsCritical = false;
                playerState.IsPlayingRevivalAnimation = false;
                playerState.KillOverride = false; // Reset kill override on successful revival

                // Set last revival time
                playerState.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // Stop and clear the revival timer
                criticalStateMainTimer?.StopTimer();
                criticalStateMainTimer = null;

                // Show successful revival notification
                NotificationManagerClass.DisplayMessageNotification(
                    "Revived by teammate! You are temporarily invulnerable but limited in movement.",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Default,
                    Color.green);

                Plugin.LogSource.LogInfo($"Team revival completed for player {playerId}");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error completing teammate revival: {ex}");
            }
        }

        /// <summary>
        /// Checks if the player has a revival item anywhere in their inventory
        /// </summary>
        public static bool HasDefib(Player player)
        {
            try
            {
                foreach (var item in player.Inventory.AllRealPlayerItems)
                {
                    if (item.TemplateId == Constants.Constants.ITEM_ID)
                    {
                        Plugin.LogSource.LogDebug($"Found defib in inventory: {item.LocalizedName()}");
                        return true;
                    }
                }

                Plugin.LogSource.LogDebug("No defib found in player inventory");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error searching for defib: {ex}");
                return false;
            }
        }

        public static Item GetDefib(Player player)
        {
            try
            {
                foreach (var item in player.Inventory.AllRealPlayerItems)
                {
                    if (item.TemplateId == Constants.Constants.ITEM_ID)
                    {
                        Plugin.LogSource.LogDebug($"Getting defib from inventory: {item.LocalizedName()}");
                        return item;
                    }
                }
                
                Plugin.LogSource.LogWarning("GetDefib called but no defib found in inventory");
                return null;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error getting defib: {ex}");
                return null;
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
                DisplayCriticalStateNotification(player);

                // Create a countdown timer for critical state with red-to-black transition
                criticalStateMainTimer = new CustomTimer();
                criticalStateMainTimer.StartCountdown(RevivalModSettings.CRITICAL_STATE_TIME.Value, "Bleeding Out", TimerPosition.MiddleCenter, TimerColorMode.RedToBlack);
            }

            RMSession.AddToCriticalPlayers(playerId);
            FikaBridge.SendPlayerCriticalStatePacket(playerId);
        }


        /// <summary>
        /// Displays critical state notification with available options
        /// </summary>
        private static void DisplayCriticalStateNotification(Player player)
        {
            try
            {
                // Build notification message
                string message = "CRITICAL CONDITION!\n";

                if (RevivalModSettings.SELF_REVIVAL_ENABLED.Value && HasDefib(player))
                {
                    message += $"Hold {RevivalModSettings.SELF_REVIVAL_KEY.Value} for 2s to use defibrillator ({(int)RevivalModSettings.SELF_REVIVE_ANIMATION_DURATION.Value}s animation)\n";
                }

                message += $"Press {RevivalModSettings.GIVE_UP_KEY.Value} to give up\n";
                message += $"Or wait for a teammate to revive you ({(int)RevivalModSettings.CRITICAL_STATE_TIME.Value} seconds)";

                NotificationManagerClass.DisplayMessageNotification(
                    message,
                    ENotificationDurationType.Long,
                    ENotificationIconType.Default,
                    Color.red);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error displaying critical state UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up critical state when exiting that state
        /// </summary>
        private static void CleanupCriticalState(Player player, string playerId)
        {
            // Stop the main critical state timer
            criticalStateMainTimer?.StopTimer();
            criticalStateMainTimer = null;

            // If player is leaving critical state without revival, clean up
            var playerState = RMSession.GetPlayerState(playerId);
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
            
            // Remove from critical players list for multiplayer sync
            RemovePlayerFromCriticalState(playerId);
            
            // Apply revival effects with limited healing
            ApplyRevivalEffects(player);

            // Apply invulnerability period
            StartInvulnerability(player);

            player.Say(EPhraseTrigger.OnMutter, false, 2f, ETagStatus.Combat, 100, true);

            // Reset critical state and kill override
            var playerState = RMSession.GetPlayerState(playerId);
            playerState.IsCritical = false;
            playerState.KillOverride = false; // Reset kill override on successful revival

            // Set last revival time
            playerState.LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            criticalStateMainTimer.StopTimer();
            criticalStateMainTimer = null;

            // Show successful revival notification
            NotificationManagerClass.DisplayMessageNotification(
                "Defibrillator used successfully! You are temporarily invulnerable but limited in movement.",
                ENotificationDurationType.Long,
                ENotificationIconType.Default,
                Color.green);

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
                    
            NotificationManagerClass.DisplayMessageNotification(
                $"Revival on cooldown! Available in {remainingCooldown} seconds",
                ENotificationDurationType.Long,
                ENotificationIconType.Alert,
                Color.yellow);

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
                    ConsumeDefibItem(player, GetDefib(player));
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

        /// <summary>
        /// Consumes a defibrillator item from the player's inventory
        /// </summary>
        private static void ConsumeDefibItem(Player player, Item defibItem)
        {
            try
            {
                if (defibItem == null)
                {
                    Plugin.LogSource.LogWarning("Cannot consume defib - item is null");
                    return;
                }

                InventoryController inventoryController = player.InventoryController;
                GStruct454 discardResult = InteractionsHandlerClass.Discard(defibItem, inventoryController, true);

                if (discardResult.Failed)
                {
                    Plugin.LogSource.LogError($"Couldn't remove item: {discardResult.Error}");
                    return;
                }
                
                inventoryController.TryRunNetworkTransaction(discardResult);
                
                Plugin.LogSource.LogInfo("Defibrillator consumed successfully");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error consuming defib item: {ex.Message}");
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

                // Severely restrict movement
                player.Physical.WalkSpeedLimit = MOVEMENT_SPEED_MULTIPLIER;

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
                if (RevivalModSettings.RESTORE_DESTROYED_BODY_PARTS.Value)
                {
                    Plugin.LogSource.LogInfo("Restoring body parts");
                    
                    foreach (EBodyPart bodyPart in Enum.GetValues(typeof(EBodyPart)))
                    {  
                        // Skip Common (overall health)
                        if (bodyPart is EBodyPart.Common)
                            continue;

                        GClass2814<ActiveHealthController.GClass2813>.BodyPartState bodyPartState = healthController.Dictionary_0[bodyPart];

                        Plugin.LogSource.LogDebug($"{bodyPart} is at {healthController.GetBodyPartHealth(bodyPart).Current} health");

                        if (!bodyPartState.IsDestroyed) 
                            continue;
                        
                        HealthValue health = bodyPartState.Health;
                        
                        bodyPartState.IsDestroyed = false;

                        // Get the appropriate percentage based on body part
                        float restorePercent = bodyPart switch
                        {
                            EBodyPart.Head => RevivalModSettings.RESTORE_HEAD_PERCENTAGE.Value / 100f,
                            EBodyPart.Chest => RevivalModSettings.RESTORE_CHEST_PERCENTAGE.Value / 100f,
                            EBodyPart.Stomach => RevivalModSettings.RESTORE_STOMACH_PERCENTAGE.Value / 100f,
                            EBodyPart.LeftArm or EBodyPart.RightArm => RevivalModSettings.RESTORE_ARMS_PERCENTAGE.Value / 100f,
                            EBodyPart.LeftLeg or EBodyPart.RightLeg => RevivalModSettings.RESTORE_LEGS_PERCENTAGE.Value / 100f,
                            _ => 0.5f // Default 50% for any unknown body parts
                        };
                        
                        float newCurrentHealth = bodyPartState.Health.Maximum * restorePercent;

                        bodyPartState.Health = new HealthValue(newCurrentHealth, bodyPartState.Health.Maximum, 0f);

                        healthController.method_43(bodyPart, EDamageType.Medicine);
                        healthController.method_35(bodyPart);
                        healthController.RemoveNegativeEffects(bodyPart);

                        var eventField = typeof(ActiveHealthController)
                            .GetField("BodyPartRestoredEvent", BindingFlags.Instance | BindingFlags.NonPublic);

                        if (eventField != null)
                        {
                            if (eventField.GetValue(healthController) is MulticastDelegate eventDelegate)
                            {
                                foreach (var handler in eventDelegate.GetInvocationList())
                                {
                                    // Pass the NEW health value after restoration, not the old one
                                    handler.DynamicInvoke(bodyPart, bodyPartState.Health.CurrentAndMaximum);
                                }
                            }
                            else
                            {
                                Plugin.LogSource.LogError("evenDelegate is null");
                            }
                        }
                        else
                        {
                            Plugin.LogSource.LogError("eventField is null");
                        }
                        Plugin.LogSource.LogDebug($"Restored {bodyPart} to {restorePercent * 100}%");
                    }
                }

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
            player.StartCoroutine(FlashInvulnerabilityEffect(player));

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
                NotificationManagerClass.DisplayMessageNotification(
                    "Temporary invulnerability has ended",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Default,
                    Color.white);
            }

            Plugin.LogSource.LogInfo($"Ended invulnerability for player {playerId}");
        }

        /// <summary>
        /// Visual effect coroutine that makes player model flash during invulnerability
        /// </summary>
        private static IEnumerator FlashInvulnerabilityEffect(Player player)
        {
            string playerId = player.ProfileId;
            bool isVisible = true;

            // Store original visibility states of all renderers
            Dictionary<Renderer, bool> originalStates = [];

            // First ensure player is visible to start
            if (player.PlayerBody?.BodySkins != null)
            {
                foreach (var kvp in player.PlayerBody.BodySkins)
                {
                    if (kvp.Value is null) 
                        continue;
                    
                    var renderers = kvp.Value.GetComponentsInChildren<Renderer>(true);
                    
                    foreach (var renderer in renderers)
                    {
                        if (renderer is null) 
                            continue;
                        
                        originalStates[renderer] = renderer.enabled;
                        renderer.enabled = true;
                    }
                }
            }

            // Flash the player model while invulnerable
            while (RMSession.GetPlayerState(playerId).IsInvulnerable)
            {
                try
                {
                    isVisible = !isVisible; // Toggle visibility

                    // Apply visibility to all renderers in the player model
                    if (player.PlayerBody?.BodySkins != null)
                    {
                        foreach (var kvp in player.PlayerBody.BodySkins)
                        {
                            if (kvp.Value is null) 
                                continue;
                            
                            var renderers = kvp.Value.GetComponentsInChildren<Renderer>(true);
                            
                            foreach (var renderer in renderers)
                            {
                                if (renderer is not null)
                                    renderer.enabled = isVisible;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError($"Error in flash effect: {ex.Message}");
                }

                yield return new WaitForSeconds(FLASH_INTERVAL);
            }

            // Ensure player is visible when effect ends
            try
            {
                foreach (var kvp in originalStates)
                {
                    if (kvp.Key is not null)
                        kvp.Key.enabled = true; // Force visibility on exit
                }
            }
            catch
            {
                // Fallback if the dictionary approach fails
                if (player.PlayerBody?.BodySkins != null)
                {
                    foreach (var kvp in player.PlayerBody.BodySkins)
                    {
                        kvp.Value?.EnableRenderers(true);
                    }
                }
            }
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
                NotificationManagerClass.DisplayMessageNotification(
                    "You have died",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Alert,
                    Color.red);

                // Get the damage type that initially caused critical state
                EDamageType damageType = playerState.PlayerDamageType;

                // Stop countdown timer
                criticalStateMainTimer?.StopTimer();
                criticalStateMainTimer = null;
                
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