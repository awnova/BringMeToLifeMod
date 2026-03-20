namespace KeepMeAlive.Server.Models.Revival;

//====================[ RevivalState ]====================
public enum RevivalState
{
    None = 0,
    BleedingOut = 1,
    Reviving = 2,
    Revived = 3,
    CoolDown = 4
}

//====================[ RevivalStateEntry ]====================
public record RevivalStateEntry
{
    public string PlayerId { get; init; } = string.Empty;
    public RevivalState State { get; set; } = RevivalState.None;
    public string ReviverId { get; set; } = string.Empty;
    public long LastUpdatedUnixSeconds { get; set; }
    public long CooldownUntilUnixSeconds { get; set; }
}

//====================[ RevivalAuthorityResponse ]====================
public record RevivalAuthorityResponse
{
    public bool Success { get; init; }
    public RevivalDeniedCode DenialCode { get; init; } = RevivalDeniedCode.None;
    public string Reason { get; init; } = string.Empty;
    public RevivalStateEntry? State { get; init; }
}

//====================[ RevivalDeniedCode ]====================
public enum RevivalDeniedCode
{
    None = 0,
    Cooldown = 1,
    InvalidState = 2,
    NotDowned = 3,
    CompleteInvalidState = 4,
    ServerError = 5
}
