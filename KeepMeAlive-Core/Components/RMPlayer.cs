//====================[ Imports ]====================
using System.Collections.Generic;
using EFT;
using KeepMeAlive.Helpers;
using UnityEngine;

namespace KeepMeAlive.Components
{
    //====================[ Enums ]====================
    public enum ReviveSource { Self = 0, Team = 1 }

    public enum RMState
    {
        None,
        CoolDown,
        BleedingOut,
        Reviving,
        Revived
    }

    //====================[ RMPlayer ]====================
    public class RMPlayer
    {
        //====================[ State ]====================
        public RMState State { get; set; } = RMState.None;
        public RMState LastObservedState { get; set; } = RMState.None;

        //====================[ Derived Flags ]====================
        public bool IsCritical => State is RMState.BleedingOut or RMState.Reviving;
        public bool IsInvulnerable => State == RMState.Revived;

        //====================[ Runtime Flags ]====================
        public bool KillOverride { get; set; }
        public bool IsReviveProgressActive { get; set; }
        public bool IsBeingRevived { get; set; }
        public bool IsSelfReviving { get; set; }
        public bool IsSilentInventoryAnimActive { get; set; }
        public bool IsSilentReviveBlurActive { get; set; }
        // True while RestorePlayerWeapon's async SetFirstAvailableItem callback is in-flight.
        // Causes the weapon-proceed-block patches to pass through so the equip can complete.
        public bool AllowWeaponEquipForReviveAnim { get; set; }

        //====================[ Session Info ]====================
        // 0 = Self, 1 = Team
        public int ReviveRequestedSource { get; set; } 
        public string CurrentReviverId { get; set; } = string.Empty;

        //====================[ Timers ]====================
        public float CriticalTimer { get; set; }
        public float InvulnerabilityTimer { get; set; }
        public float CooldownTimer { get; set; }
        // Countdown until next periodic state resync broadcast. -1 triggers immediate send.
        public float ResyncCooldown { get; set; }
        // Watchdog: counts down from a set value when IsBeingRevived is raised from a TeamHelp packet.
        // If it reaches 0 while State == BleedingOut the reviver is considered timed-out / disconnected.
        public float BeingRevivedWatchdogTimer { get; set; }

        //====================[ Stored Values ]====================
        public float OriginalAwareness { get; set; } = -1f;
        public bool HasStoredAwareness { get; set; }
        public float OriginalMovementSpeed { get; set; } = -1f;
        public long LastRevivalTimesByPlayer { get; set; }
        public EDamageType PlayerDamageType { get; set; } = EDamageType.Undefined;

        //====================[ UI Timers ]====================
        public CustomTimer CriticalStateMainTimer { get; set; }
        public CustomTimer RevivePromptTimer { get; set; }

        //====================[ Coroutine Handles ]====================
        // Stored so ExitDowned / EnterDowned can cancel the revive-progress coroutine
        // before it fires for a stale session when the player re-enters downed quickly.
        public Coroutine ReviveProgressCoroutine { get; set; }

        //====================[ Input Tracking ]====================
        public Dictionary<KeyCode, float> SelfRevivalKeyHoldDuration { get; set; } = new();
        // Deterministic self-revive flow state (hold -> auth -> reviving).
        public float SelfReviveHoldTime { get; set; }
        public bool SelfReviveCommitted { get; set; }
        public bool SelfReviveAuthPending { get; set; }
        public int SelfReviveAttemptId { get; set; }

        //====================[ Revive Flow Markers ]====================
        // Incremented whenever a new downed cycle begins.
        public int ReviveCycleId { get; set; }
        // Set to ReviveCycleId once restore/finalize side effects are committed.
        public int FinalizedReviveCycleId { get; set; } = -1;
        public bool IsReviveFinalizeCommittedForCurrentCycle => FinalizedReviveCycleId == ReviveCycleId;
    }
}