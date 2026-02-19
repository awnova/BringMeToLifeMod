using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using RevivalMod.Helpers;
using UnityEngine;

namespace RevivalMod.Components
{
    public enum RMState
    {
        None,
        CoolDown,
        BleedingOut,
        Reviving,
        Revived
    }

    public class RMPlayer
    {
        // Authoritative state
        public RMState State { get; set; } = RMState.None;

        // Derived flags
        public bool IsCritical => State is RMState.BleedingOut or RMState.Reviving;
        public bool IsInvulnerable => State == RMState.Revived;

        // Runtime flags
        public bool KillOverride { get; set; }
        public bool IsPlayingRevivalAnimation { get; set; }
        public bool IsBeingRevived { get; set; }

        // Session / request info
        public bool RevivalRequested { get; set; }
        public int ReviveRequestedSource { get; set; } // 0 = Self, 1 = Team
        public string CurrentReviverId { get; set; } = string.Empty;

        // Timers
        public float CriticalTimer { get; set; }
        public float InvulnerabilityTimer { get; set; }
        public float CooldownTimer { get; set; }

        // Stored gameplay values
        public float OriginalAwareness { get; set; } = -1f;
        public bool HasStoredAwareness { get; set; }
        public float OriginalMovementSpeed { get; set; } = -1f;
        public long LastRevivalTimesByPlayer { get; set; }
        public EDamageType PlayerDamageType { get; set; } = EDamageType.Undefined;

        // Cached fake items for revival animations
        public Item FakeCmsItem { get; set; }
        public Item FakeSurvKitItem { get; set; }

        // Per-player UI timers
        public CustomTimer CriticalStateMainTimer { get; set; }
        public CustomTimer RevivePromptTimer { get; set; }

        // Input tracking
        public Dictionary<KeyCode, float> SelfRevivalKeyHoldDuration { get; set; } = new();
    }
}
