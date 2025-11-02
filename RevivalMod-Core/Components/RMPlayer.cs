//====================[ Imports ]====================
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using RevivalMod.Helpers;
using UnityEngine;

namespace RevivalMod.Components
{
    //====================[ RMState ]====================
    public enum RMState
    {
        None,        // Normal gameplay (not in any revival-related state)
        CoolDown,    // Revival cooldown period after invulnerability ends
        BleedingOut, // Player is downed and bleeding out
        Reviving,    // Revival animation in progress
        Revived      // Revived and invulnerable
    }

    //====================[ RMPlayer ]====================
    public class RMPlayer
    {
        //====================[ Authoritative State ]====================
        // Single source of truth for player revival state.
        public RMState State { get; set; } = RMState.None;

        //====================[ Derived Flags ]====================
        // Derived directly from State (do not set manually).
        public bool IsCritical => State == RMState.BleedingOut || State == RMState.Reviving;

        // Invulnerability only during Revived state (post-revival grace).
        // While BleedingOut: damage may still apply unless GodMode config intervenes.
        public bool IsInvulnerable => State == RMState.Revived;

        //====================[ Runtime Flags ]====================
        public bool KillOverride { get; set; } = false;
        public bool IsPlayingRevivalAnimation { get; set; } = false;
        // True during both the 2s teammate hold and the actual revival animation.
        public bool IsBeingRevived { get; set; } = false;

        //====================[ Session / Request Info ]====================
        // one-shot: set true to start local revive
        public bool   RevivalRequested      { get; set; } = false;
        // 0 = Self, 1 = Team
        public int    ReviveRequestedSource { get; set; } = 0;
        // ProfileId of player performing the revival (empty for self-revive)
        public string CurrentReviverId      { get; set; } = string.Empty;

        //====================[ Timers ]====================
        public float CriticalTimer       { get; set; } = 0f;
        public float InvulnerabilityTimer{ get; set; } = 0f;
        public float CooldownTimer       { get; set; } = 0f;

        //====================[ Stored Gameplay Values ]====================
        public float OriginalAwareness       { get; set; } = -1f;
        public bool  HasStoredAwareness      { get; set; } = false;
        public float OriginalMovementSpeed   { get; set; } = -1f;
        public long  LastRevivalTimesByPlayer{ get; set; } = 0;
        public EDamageType PlayerDamageType  { get; set; } = EDamageType.Undefined;

        //====================[ Cached Fake Items (Animations) ]====================
        // Network-coordinated placeholder items for revival animations.
        public Item FakeCmsItem     { get; set; } = null;
        public Item FakeSurvKitItem { get; set; } = null;

        //====================[ Per-Player UI Timers ]====================
        public CustomTimer CriticalStateMainTimer { get; set; }
        public CustomTimer RevivePromptTimer      { get; set; }

        //====================[ Input Tracking ]====================
        // Key hold durations for self-revival initiation.
        public Dictionary<KeyCode, float> SelfRevivalKeyHoldDuration { get; set; } = new();
    }
}
