using EFT;
using RevivalMod.Helpers;
using System.Collections.Generic;
using UnityEngine;

namespace RevivalMod.Components
{
    public class RMPlayer()
    {
        // State flags
        public bool IsCritical { get; set; } = false;
        public bool IsInvulnerable { get; set; } = false;
        public bool KillOverride { get; set; } = false;
        public bool IsPlayingRevivalAnimation { get; set; } = false;
        public bool IsBeingRevived { get; set; } = false; // True during both the 2-second hold AND the actual revival animation
        
        // Timers
        public float CriticalTimer { get; set; } = 0f;
        public float InvulnerabilityTimer { get; set; } = 0f;
        
        // Stored values
        public float OriginalAwareness { get; set; } = -1f;
        public bool HasStoredAwareness { get; set; } = false;
        public float OriginalMovementSpeed { get; set; } = -1f;
        public long LastRevivalTimesByPlayer { get; set; } = 0;
        public EDamageType PlayerDamageType { get; set; } = EDamageType.Undefined;
        
        // Per-player UI timers (moved from Features.cs)
        public CustomTimer CriticalStateMainTimer { get; set; }
        public CustomTimer RevivePromptTimer { get; set; }
        
        // Key hold duration tracking for self-revival (moved from Features.cs)
        public Dictionary<KeyCode, float> SelfRevivalKeyHoldDuration { get; set; } = new();
    }
}