//====================[ Imports ]====================
using KeepMeAlive.Server.Models.Revival;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

namespace KeepMeAlive.Server.Services;

//====================[ RevivalStateService ]====================
[Injectable(InjectionType.Singleton)]
public class RevivalStateService(ISptLogger<RevivalStateService> logger, RevivalConfigService configService)
{
    //====================[ State ]====================
    private readonly Dictionary<string, RevivalStateEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    //====================[ In-Memory Lifecycle ]====================
    public void Load()
    {
        // Intentionally no-op: revival authority state is RAM-only and should not persist.
    }

    public void Save()
    {
        // Intentionally no-op: revival authority state is RAM-only and should not persist.
    }

    //====================[ State API ]====================
    public RevivalStateEntry GetOrCreate(string playerId)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(playerId, out var entry))
            {
                entry = new RevivalStateEntry { PlayerId = playerId, LastUpdatedUnixSeconds = Now() };
                _entries[playerId] = entry;
            }

            return entry;
        }
    }

    public RevivalStateEntry SetBleedingOut(string playerId)
    {
        var entry = GetOrCreate(playerId);
        lock (_sync)
        {
            entry.State = RevivalState.BleedingOut;
            entry.ReviverId = string.Empty;
            entry.LastUpdatedUnixSeconds = Now();
            entry.CooldownUntilUnixSeconds = 0;
        }
        Save();
        return entry;
    }

    //====================[ Authority Flow ]====================
    public RevivalAuthorityResponse TryStartRevive(string playerId, string reviverId, string source)
    {
        var entry = GetOrCreate(playerId);
        lock (_sync)
        {
            var now = Now();

            if (entry.State == RevivalState.CoolDown && entry.CooldownUntilUnixSeconds > now)
            {
                return Denied(RevivalDeniedCode.Cooldown, "Player on cooldown", entry);
            }

            // Allow a self-revive to restart if the previous attempt left the server in
            // Reviving state but the animation never completed (e.g. client-side failure).
            // A self-revive is identified by playerId == reviverId.
            bool isSelfRevive = source.Equals("self", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(playerId, reviverId, StringComparison.Ordinal);

            if (entry.State is RevivalState.Reviving or RevivalState.Revived)
            {
                if (entry.State == RevivalState.Reviving && isSelfRevive)
                {
                    // Idempotent re-entry: allow the client to restart a self-revive that
                    // got stuck. Fall through to set state = Reviving again.
                    logger.Info($"[KeepMeAlive] Re-entering Reviving state for self-revive: {playerId}");
                }
                else
                {
                    return Denied(RevivalDeniedCode.InvalidState, $"Invalid state for revive start: {entry.State}", entry);
                }
            }

            // Allow None state as well as BleedingOut: begin-critical is fire-and-forget and
            // may not have arrived yet when the reviver's 2-second hold completes, especially
            // with two players downed simultaneously. The reviver is standing over the body so
            // we trust the client-side downed state.
            if (entry.State != RevivalState.BleedingOut &&
                entry.State != RevivalState.Reviving   &&
                entry.State != RevivalState.None)
            {
                return Denied(RevivalDeniedCode.NotDowned, $"Player is not downed: {entry.State}", entry);
            }

            if (entry.State == RevivalState.None)
            {
                logger.Warning($"[KeepMeAlive] request-revive-start for {playerId} arrived before begin-critical (state=None). Allowing and promoting to Reviving.");
            }

            entry.State = RevivalState.Reviving;
            entry.ReviverId = source.Equals("self", StringComparison.OrdinalIgnoreCase) ? playerId : reviverId;
            entry.LastUpdatedUnixSeconds = now;
        }
        Save();
        return Allowed(entry);
    }

    public RevivalAuthorityResponse TryCompleteRevive(string playerId, string reviverId)
    {
        var entry = GetOrCreate(playerId);
        lock (_sync)
        {
            if (entry.State != RevivalState.Reviving)
            {
                return Denied(RevivalDeniedCode.CompleteInvalidState, $"Cannot complete revive from {entry.State}", entry);
            }

            entry.State = RevivalState.Revived;
            entry.ReviverId = reviverId;
            entry.LastUpdatedUnixSeconds = Now();
        }
        Save();
        return Allowed(entry);
    }

    //====================[ Post-Revival State ]====================
    public RevivalStateEntry MarkCooldown(string playerId, float cooldownSeconds)
    {
        var entry = GetOrCreate(playerId);
        lock (_sync)
        {
            entry.State = RevivalState.CoolDown;
            entry.LastUpdatedUnixSeconds = Now();
            entry.CooldownUntilUnixSeconds = entry.LastUpdatedUnixSeconds + (long)Math.Max(0, cooldownSeconds);
            entry.ReviverId = string.Empty;
        }
        Save();
        return entry;
    }

    public RevivalStateEntry Reset(string playerId)
    {
        var entry = GetOrCreate(playerId);
        lock (_sync)
        {
            entry.State = RevivalState.None;
            entry.LastUpdatedUnixSeconds = Now();
            entry.CooldownUntilUnixSeconds = 0;
            entry.ReviverId = string.Empty;
        }
        Save();
        return entry;
    }

    //====================[ Response Helpers ]====================
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static RevivalAuthorityResponse Allowed(RevivalStateEntry state) =>
        new() { Success = true, State = state };

    private static RevivalAuthorityResponse Denied(RevivalDeniedCode code, string reason, RevivalStateEntry state) =>
        new() { Success = false, DenialCode = code, Reason = reason, State = state };
}
