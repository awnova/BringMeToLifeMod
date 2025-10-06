using EFT;

namespace RevivalMod.Components
{
    public class RevivalModPlayer(Player player) : Player
    {
        public new string ProfileId { get; set; } = player.ProfileId;
        public bool IsInCriticalState { get; set; } = false;
        public float CriticalTimer { get; set; } = 25f;
    }
}