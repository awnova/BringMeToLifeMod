namespace RevivalMod.Server.Models.Revival;

public enum RevivalState
{
    None = 0,
    BleedingOut = 1,
    Reviving = 2,
    Revived = 3,
    CoolDown = 4
}

public record RevivalStateEntry
{
    public string PlayerId { get; init; } = string.Empty;
    public RevivalState State { get; set; } = RevivalState.None;
    public string ReviverId { get; set; } = string.Empty;
    public long LastUpdatedUnixSeconds { get; set; }
    public long CooldownUntilUnixSeconds { get; set; }
}

public record RevivalAuthorityResponse
{
    public bool Success { get; init; }
    public string Reason { get; init; } = string.Empty;
    public RevivalStateEntry? State { get; init; }
}
