using EFT;

namespace RevivalMod.Components
{
    public class RMPlayer()
    {
        public bool IsCritical { get; set; } = false;
        public bool IsInvulnerable { get; set; } = false;
        public bool KillOverride { get; set; } = false;
        public bool IsPlayingRevivalAnimation { get; set; } = false;
        public float CriticalTimer { get; set; } = 0f;
        public float InvulnerabilityTimer { get; set; } = 0f;
        public float OriginalAwareness { get; set; } = -1f;
        public bool HasStoredAwareness { get; set; } = false;
        public float OriginalMovementSpeed { get; set; } = -1f;
        public long LastRevivalTimesByPlayer { get; set; } = 0;
        public EDamageType PlayerDamageType { get; set; } = EDamageType.Undefined;
    }
}