using EFT;
using EFT.HealthSystem;
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

        #region State Tracking

        // Key holding tracking
        private static readonly Dictionary<KeyCode, float> _selfRevivalKeyHoldDuration = [];

        // Player state dictionaries
        public static Dictionary<string, RMPlayer> _playerList = [];

        // Reference to local player
        private static Player PlayerClient { get; set; }

        #endregion
        public static CustomTimer criticalStateMainTimer;

        private static float _nextActionTime = 0;

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
                if (!PlayerClient.IsYourPlayer || !_playerList.ContainsKey(playerId))
                    return;

                // Process player states
                ProcessInvulnerabilityState(PlayerClient, playerId);
                ProcessCriticalState(PlayerClient, playerId);

                // Send position updates if in critical state (regardless of local player status)
                if (IsPlayerInCriticalState(playerId) && Time.time >= _nextActionTime)
                {
                    _nextActionTime = Time.time + 0.05f;
                    FikaBridge.SendPlayerPositionPacket(playerId, new DateTime(), PlayerClient.Position);
                }
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
            if (!_playerList[playerId].IsInvulnerable)
                return;

            float timer = _playerList[playerId].InvulnerabilityTimer;

            if (!(timer > 0)) 
                return;
            
            timer -= Time.deltaTime;
            _playerList[playerId].InvulnerabilityTimer = timer;

            // Apply invulnerability restrictions
            ApplyInvulnerabilityRestrictions(player);

            // End invulnerability if timer is up
            if (timer <= 0)
            {
                EndInvulnerability(player);
            }
        }

        /// <summary>
        /// Applies movement and action restrictions during invulnerability period
        /// </summary>
        private static void ApplyInvulnerabilityRestrictions(Player player)
        {
            // Force player to crouch during invulnerability if enabled
            if (FORCE_CROUCH_DURING_INVULNERABILITY && player.MovementContext.PoseLevel > 0)
            {
                player.MovementContext.SetPoseLevel(0);
            }

            // Disable shooting during invulnerability if enabled
            if (DISABLE_SHOOTING_DURING_INVULNERABILITY && player.HandsController.IsAiming)
            {
                player.HandsController.IsAiming = false;
            }
        }

        /// <summary>
        /// Processes critical state and checks for revival inputs
        /// </summary>
        private static void ProcessCriticalState(Player player, string playerId)
        {
            // Check if player is in critical state
            if (!_playerList[playerId].IsCritical)
                return;

            // Store original movement speed if not already stored
            if (_playerList[playerId].OriginalMovementSpeed < 0)
                _playerList[playerId].OriginalMovementSpeed = player.Physical.WalkSpeedLimit;

            // Severely restrict movement
            player.Physical.WalkSpeedLimit = MOVEMENT_SPEED_MULTIPLIER;

            // Force player to crouch
            if (player.MovementContext != null)
            {
                player.HandsController.IsAiming = false;
                player.MovementContext.SetPoseLevel(0f, true);
                player.MovementContext.IsInPronePose = true;
                player.ActiveHealthController.SetStaminaCoeff(1f);
                player.SetEmptyHands(null);
            }
            else
            {
                Plugin.LogSource.LogError("player.MovementContext is null!");
            }

            if (criticalStateMainTimer.IsRunning)
                _playerList[playerId].CriticalTimer -= Time.deltaTime;

            // Update the main critical state timer
            criticalStateMainTimer?.Update();

            // If time runs out, player dies
            // If in hardcore mode, all players are in critical states, everybody dies
            // bool isLastManStanding = false;

            // TODO: need testing
            /* if (RMSession.GetTotalPlayers() == RMSession.GetCriticalPlayers().Count)
                isLastManStanding = true;*/

            //Plugin.LogSource.LogDebug("isLastManStanding:" + isLastManStanding);

            if (_playerList[playerId].CriticalTimer <= 0)
            {
                criticalStateMainTimer?.StopTimer();
                criticalStateMainTimer = null;

                ForcePlayerDeath(player);
                return;
            }

            // Check for self-revival if player has the item
            CheckForSelfRevival(player);

            // Check for "give up" key press
            if (Input.GetKeyDown(RevivalModSettings.GIVE_UP_KEY.Value))
                ForcePlayerDeath(player);
        }

        /// <summary>
        /// Checks if self-revival is possible and processes key input with hold-to-revive behavior
        /// </summary>
        private static void CheckForSelfRevival(Player player)
        {
            bool revivalItemCheck = HasDefib(player.Inventory.GetPlayerItems(EPlayerItems.Equipment));
            GamePlayerOwner owner = player.GetComponentInParent<GamePlayerOwner>();

            if (!revivalItemCheck || 
                !RevivalModSettings.SELF_REVIVAL_ENABLED.Value) 
                return;
            
            KeyCode revivalKey = RevivalModSettings.SELF_REVIVAL_KEY.Value;

            // Start key hold tracking when key is first pressed
            if (Input.GetKeyDown(revivalKey))
            {
                _selfRevivalKeyHoldDuration[revivalKey] = 0f;
                
                criticalStateMainTimer?.StopTimer();
                
                // Show revive timer
                owner.ShowObjectivesPanel("Reviving {0:F1}", RevivalModSettings.REVIVAL_HOLD_DURATION.Value);
            }

            // Update hold duration while key is held
            if (Input.GetKey(revivalKey) && 
                _selfRevivalKeyHoldDuration.ContainsKey(revivalKey))
            {
                _selfRevivalKeyHoldDuration[revivalKey] += Time.deltaTime;

                float holdDuration = _selfRevivalKeyHoldDuration[revivalKey];
                float requiredDuration = RevivalModSettings.REVIVAL_HOLD_DURATION.Value;           

                // Trigger revival when key is held long enough
                if (holdDuration >= requiredDuration)
                {
                    _selfRevivalKeyHoldDuration.Remove(revivalKey);
                    
                    criticalStateMainTimer?.StopTimer();

                    TryPerformManualRevival(player);
                }
            }

            // Continue if key is still being held
            if (!Input.GetKeyUp(revivalKey) ||
                !_selfRevivalKeyHoldDuration.ContainsKey(revivalKey))
                return;
            
            // Close revive timer
            owner.CloseObjectivesPanel();
            
            // Resume critical state timer
            criticalStateMainTimer?.StartCountdown(RevivalFeatures._playerList[player.ProfileId].CriticalTimer,"Critical State Timer", TimerPosition.MiddleCenter);

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
            return _playerList[playerId].IsCritical;
        }

        public static void ClearPlayerCritcalStateTimer(string playerId)
        {
            _playerList[playerId].CriticalTimer = 0f;
        }

        public static void ClearPlayerInCriticalState(string playerId)
        {
            _playerList[playerId].IsCritical = false;
        }

        /// <summary>
        /// Checks if a player is currently invulnerable
        /// </summary>
        public static bool IsPlayerInvulnerable(string playerId)
        {
            return _playerList[playerId].IsInvulnerable;
        }

        /// <summary>
        /// Sets a player's critical state status
        /// </summary>
        public static void SetPlayerCriticalState(Player player, bool criticalState, EDamageType damageType)
        {
            // Null guard player
            if (player == null)
                return;

            string playerId = player.ProfileId;

            // Keeps track of critical state and damage type in dictionaries
            _playerList[playerId].IsCritical = criticalState;
            _playerList[playerId].PlayerDamageType = damageType;

            if (criticalState)
            {
                InitializeCriticalState(player, playerId);
            }
            else
            {
                CleanupCriticalState(player, playerId);
            }
        }

        /// <summary>
        /// Attempts to perform revival of player by a teammate
        /// </summary>
        public static bool TryPerformRevivalByTeammate(string playerId)
        {
            if (playerId != Singleton<GameWorld>.Instance.MainPlayer.ProfileId)
                return false;

            Player player = Singleton<GameWorld>.Instance.MainPlayer;

            try
            {
                // Apply revival effects
                ApplyRevivalEffects(player);

                // Apply invulnerability
                StartInvulnerability(player);

                // Reset critical state
                _playerList[playerId].IsCritical = false;

                // Set last revival time
                _playerList[playerId].LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // Show successful revival notification
                NotificationManagerClass.DisplayMessageNotification(
                    "Defibrillator used successfully! You are temporarily invulnerable but limited in movement.",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Default,
                    Color.green);

                criticalStateMainTimer.StopTimer();
                criticalStateMainTimer = null;

                Plugin.LogSource.LogInfo($"Team revival performed for player {playerId}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in teammate revival: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the player has a revival item in their inventory
        /// </summary>
        public static bool HasDefib(IEnumerable<Item> inRaidItems)
        {
            foreach(Item item in inRaidItems)
            {
                if (item.TemplateId == Constants.Constants.ITEM_ID)
                    return true;
            }

            return false;
        }

        #endregion

        #region Revival Implementation

        /// <summary>
        /// Initializes critical state for a player
        /// </summary>
        private static void InitializeCriticalState(Player player, string playerId)
        {
            // Set the critical state timer
            _playerList[playerId].CriticalTimer = RevivalModSettings.TIME_TO_REVIVE.Value;
            _playerList[playerId].IsInvulnerable = true;

            // Apply effects and make player revivable
            ApplyCriticalEffects(player);
            ApplyRevivableState(player);

            // Show UI notification for local player
            if (player.IsYourPlayer)
            {
                DisplayCriticalStateNotification(player);

                // Create a countdown timer for critical state
                criticalStateMainTimer = new CustomTimer();
                criticalStateMainTimer.StartCountdown(RevivalModSettings.TIME_TO_REVIVE.Value, "Critical State Timer", TimerPosition.MiddleCenter);
            }

            RMSession.AddToCriticalPlayers(playerId, player.Position);

            // Send initial position packet for multiplayer sync
            FikaBridge.SendPlayerPositionPacket(playerId, new DateTime(), player.Position);
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

                if (RevivalModSettings.SELF_REVIVAL_ENABLED.Value && HasDefib(player.Inventory.GetPlayerItems(EPlayerItems.Equipment)))
                    message += $"Hold {RevivalModSettings.SELF_REVIVAL_KEY.Value} for {RevivalModSettings.REVIVAL_HOLD_DURATION.Value}s to use defibrillator\n";

                message += $"Press {RevivalModSettings.GIVE_UP_KEY.Value} to give up\n";
                message += $"Or wait for a teammate to revive you ({(int)RevivalModSettings.TIME_TO_REVIVE.Value} seconds)";

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
            if (!(_playerList[playerId].InvulnerabilityTimer <= 0)) 
                return;
            
            RemoveRevivableState(player);
            RestorePlayerMovement(player);
        }

        /// <summary>
        /// Attempts to perform manual revival
        /// </summary>
        public static bool TryPerformManualRevival(Player player)
        {
            if (player is null)
                return false;

            string playerId = player.ProfileId;

            // Check if the player has the revival item
            bool hasDefib = HasDefib(player.Inventory.GetPlayerItems(EPlayerItems.Equipment));

            // Check if revival is on cooldown
            if (IsRevivalOnCooldown(playerId))
                return false;

            if (hasDefib || 
                RevivalModSettings.TESTING.Value)
                return PerformRevival(player, playerId);
            
            NotificationManagerClass.DisplayMessageNotification(
                "No defibrillator found! Unable to revive!",
                ENotificationDurationType.Long,
                ENotificationIconType.Alert,
                Color.red);

            return false;
        }

        /// <summary>
        /// Checks if revival is on cooldown and shows notification if needed
        /// </summary>
        public static bool IsRevivalOnCooldown(string playerId)
        {
            long lastRevivalTime = _playerList[playerId].LastRevivalTimesByPlayer;
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool isOnCooldown = (currentTime - lastRevivalTime) < RevivalModSettings.REVIVAL_COOLDOWN.Value;

            if (!isOnCooldown ||
                RevivalModSettings.TESTING.Value) 
                return false;
            
            // Only show notification if not in test mode
            int remainingCooldown = (int)(RevivalModSettings.REVIVAL_COOLDOWN.Value - (currentTime - lastRevivalTime));
                    
            NotificationManagerClass.DisplayMessageNotification(
                $"Revival on cooldown! Available in {remainingCooldown} seconds",
                ENotificationDurationType.Long,
                ENotificationIconType.Alert,
                Color.yellow);

            return true;
        }

        /// <summary>
        /// Performs the actual revival process
        /// </summary>
        private static bool PerformRevival(Player player, string playerId)
        {
            // Consume the item if not in test mode
            ConsumeDefibItem(player);

            // Remove from critical players list for multiplayer sync
            FikaBridge.SendRemovePlayerFromCriticalPlayersListPacket(playerId);

            // Apply revival effects with limited healing
            ApplyRevivalEffects(player);

            // Apply invulnerability period
            StartInvulnerability(player);

            player.Say(EPhraseTrigger.OnMutter, false, 2f, ETagStatus.Combat, 100, true);

            // Reset critical state
            _playerList[playerId].IsCritical = false;

            // Set last revival time
            _playerList[playerId].LastRevivalTimesByPlayer = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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
        /// Revives a teammate by a player with a defibrillator
        /// </summary>
        public static bool PerformTeammateRevival(string targetPlayerId, Player player)
        {
            try
            {
                NotificationManagerClass.DisplayMessageNotification(
                   "Reviving teammate...",
                   ENotificationDurationType.Default,
                   ENotificationIconType.Friend,
                   Color.green);

                ConsumeDefibItem(player);

                RMSession.RemovePlayerFromCriticalPlayers(targetPlayerId);

                FikaBridge.SendRemovePlayerFromCriticalPlayersListPacket(targetPlayerId);
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
        private static void ConsumeDefibItem(Player player)
        {
            try
            {
                var inRaidItems = player.Inventory.GetPlayerItems(EPlayerItems.Equipment);
                Item defibItem = null;

                foreach (Item item in inRaidItems)
                {
                    if (item.TemplateId == Constants.Constants.ITEM_ID)
                        defibItem = item;
                }

                if (defibItem == null)
                {
                    Plugin.LogSource.LogWarning("No defibrillator item found to consume");
                    return;
                }

                Plugin.LogSource.LogDebug($"Found defib item: {defibItem.TemplateId}");

                InventoryController inventoryController = player.InventoryController;
                GStruct454 discardResult = InteractionsHandlerClass.Discard(defibItem, inventoryController, true);

                if (discardResult.Failed)
                {
                    Logger.LogError($"Couldn't remove item: {discardResult.Error}");
                    return;
                }

                inventoryController.TryRunNetworkTransaction(discardResult, null);
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

                // Store original movement speed if not already stored
                if (_playerList[playerId].OriginalMovementSpeed < 0)
                    _playerList[playerId].OriginalMovementSpeed = player.Physical.WalkSpeedLimit;

                // Apply visual and movement effects
                if (RevivalModSettings.CONTUSION_EFFECT.Value)
                    player.ActiveHealthController.DoContusion(RevivalModSettings.REVIVAL_DURATION.Value, 1f);
                
                if (RevivalModSettings.STUN_EFFECT.Value)
                    player.ActiveHealthController.DoStun(RevivalModSettings.REVIVAL_DURATION.Value / 2, 1f);

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

                // Restore original movement speed if we stored it
                player.Physical.WalkSpeedLimit = _playerList[playerId].OriginalMovementSpeed;

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

                // Skip if already applied
                if (_playerList[playerId].OriginalAwareness == 0)
                    return;

                // Store original awareness value
                _playerList[playerId].OriginalAwareness = player.Awareness;

                // Configure player for revivable state
                player.Awareness = 0f; 
                player.PlayDeathSound();
                player.HandsController.IsAiming = false;
                player.MovementContext.EnableSprint(false);
                player.MovementContext.SetPoseLevel(0f, true);
                player.MovementContext.IsInPronePose = true;
                player.SetEmptyHands(null);

                // Make player invisible to AI and mark as dead
                // But for hardcore mode, we want them to still be targetable ?
                player.ActiveHealthController.IsAlive = true;

                // Enable God mode
                if (RevivalModSettings.PLAYER_ALIVE.Value)
                    player.ActiveHealthController.IsAlive = false;
                
                if (RevivalModSettings.GOD_MODE.Value)
                    player.ActiveHealthController.SetDamageCoeff(0);

                GClass3756.ReleaseBeginSample("Player.OnDead.SoundWork", "OnDead");
                
                if (player.ShouldVocalizeDeath(player.LastDamagedBodyPart))
                {
                    EPhraseTrigger trigger = player.LastDamageType.IsWeaponInduced() ? EPhraseTrigger.OnDeath : EPhraseTrigger.OnAgony;
                    try
                    {
                        player.Speaker.Play(trigger, player.HealthStatus, true, null);
                        goto IL_12F;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(ex.Message);
                        goto IL_12F;
                    }
                }
IL_12F:
                player.MovementContext.ReleaseDoorIfInteractingWithOne();
                player.MovementContext.OnStateChanged -= player.method_17;
                player.MovementContext.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;

                player.MovementContext.StationaryWeapon?.Unlock(player.ProfileId);
                if (player.MovementContext.StationaryWeapon != null && player.MovementContext.StationaryWeapon.Item == player.HandsController.Item)
                {
                    player.MovementContext.StationaryWeapon.Show();
                    player.ReleaseHand();
                    return;
                }

                // Not sure what this does yet
                // Use reflection to access protected members to create and assign a corpse
                /* try
                {
                    // Get the protected Corpse property
                    FieldInfo corpseField = null;
                    for (Type t = player.GetType(); t != null; t = t.BaseType)
                    {
                        corpseField = t.GetField("Corpse",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (corpseField != null) break;
                    }
                    if (corpseField != null)
                    {
                        Corpse corpse = player.CreateCorpse();

                        // Set the Corpse property with the new corpse

                        Plugin.LogSource.LogDebug($"Created and assigned corpse for player {playerId}");
                    }
                    else
                    {
                        Plugin.LogSource.LogWarning("Could not find Corpse property via reflection");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError($"Error creating corpse: {ex.Message}");
                } */

                // Send initial position for multiplayer sync
                //FikaBridge.SendPlayerPositionPacket(playerId, new DateTime(), player.Position);

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

                if (_playerList[playerId].OriginalAwareness != 0)
                    return;

                // Restore awareness and visibility
                player.Awareness = _playerList[playerId].OriginalAwareness;
                player.IsVisible = true;
                player.ActiveHealthController.IsAlive = true;

                if (RevivalModSettings.CONTUSION_EFFECT.Value)
                    player.ActiveHealthController.DoContusion(25f, 0.25f);
                
                player.Awareness = 350f;

                Plugin.LogSource.LogInfo($"Removed revivable state from player {playerId}");
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

                if (healthController == null)
                {
                    Plugin.LogSource.LogError("Could not get ActiveHealthController");
                    return;
                }

                // Make player targetable by AI
                healthController.IsAlive = true;

                // Disable God mode
                if (RevivalModSettings.GOD_MODE.Value)
                    healthController.SetDamageCoeff(1);

                // Restore destroyed body parts if setting enabled
                if (RevivalModSettings.RESTORE_DESTROYED_BODY_PARTS.Value)
                {
                    foreach (EBodyPart bodyPart in Enum.GetValues(typeof(EBodyPart)))
                    {  
                        if (bodyPart == EBodyPart.Common)
                            continue;

                        GClass2814<ActiveHealthController.GClass2813>.BodyPartState bodyPartState = healthController.Dictionary_0[bodyPart];

                        Plugin.LogSource.LogDebug($"{bodyPart} is at {healthController.GetBodyPartHealth(bodyPart).Current} health");

                        if (bodyPartState.IsDestroyed)
                        {

                            HealthValue health = bodyPartState.Health;
                            bodyPartState.IsDestroyed = false;

                            float restorePercent = RevivalModSettings.RESTORE_DESTROYED_BODY_PARTS_AMOUNT.Value/100f;
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
                                        handler.DynamicInvoke(bodyPart, health.CurrentAndMaximum);
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
                            Plugin.LogSource.LogDebug($"Restored {bodyPart}");
                        }
                    }
                }

                // Apply disorientation effects
                if (RevivalModSettings.CONTUSION_EFFECT.Value)
                    healthController.DoContusion(RevivalModSettings.REVIVAL_DURATION.Value, 1f);
                    
                if (RevivalModSettings.STUN_EFFECT.Value) 
                    healthController.DoStun(RevivalModSettings.REVIVAL_DURATION.Value / 2, 1f);

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

            _playerList[playerId].IsInvulnerable = true;
            _playerList[playerId].InvulnerabilityTimer = RevivalModSettings.REVIVAL_DURATION.Value;

            // Apply movement restrictions
            ApplyCriticalEffects(player);

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

            _playerList[playerId].IsInvulnerable = false;
            
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
            while (_playerList[playerId].IsInvulnerable)
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
                        if (kvp.Value is not null)
                            kvp.Value.EnableRenderers(true);
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

                // Add to override list first (before any other operations)
                _playerList[playerId].KillOverride = true;

                // Clean up all state tracking for this player
                _playerList[playerId].IsInvulnerable = false;
                _playerList[playerId].IsCritical = false;

                // Remove player from critical players list for network sync                

                // Show notification about death
                NotificationManagerClass.DisplayMessageNotification(
                    "You have died",
                    ENotificationDurationType.Long,
                    ENotificationIconType.Alert,
                    Color.red);

                // Get the damage type that initially caused critical state
                EDamageType damageType = _playerList[playerId].PlayerDamageType;

                // Stop countdown timer
                criticalStateMainTimer?.StopTimer();
                criticalStateMainTimer = null;

                // Use original Kill method, bypassing our patch
                player.ActiveHealthController.IsAlive = true;
                player.ActiveHealthController.Kill(damageType);

                RMSession.RemovePlayerFromCriticalPlayers(playerId);
                FikaBridge.SendRemovePlayerFromCriticalPlayersListPacket(playerId);

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