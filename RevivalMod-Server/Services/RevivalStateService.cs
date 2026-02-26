using System.Text.Json;
using RevivalMod.Server.Models.Revival;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

namespace RevivalMod.Server.Services;

[Injectable(InjectionType.Singleton)]
public class RevivalStateService(ISptLogger<RevivalStateService> logger, RevivalConfigService configService)
{
    private readonly Dictionary<string, RevivalStateEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private string StateFilePath => Path.Combine(configService.ModPath, "revival-state.json");

    public void Load()
    {
        try
        {
            if (!File.Exists(StateFilePath))
            {
                return;
            }

            var json = File.ReadAllText(StateFilePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, RevivalStateEntry>>(json);
            if (data is null)
            {
                return;
            }

            lock (_sync)
            {
                _entries.Clear();
                foreach (var kv in data)
                {
                    _entries[kv.Key] = kv.Value;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[RevivalMod.Server] Failed to load state file: {ex.Message}");
        }
    }

    public void Save()
    {
        try
        {
            Dictionary<string, RevivalStateEntry> snapshot;
            lock (_sync)
            {
                snapshot = _entries.ToDictionary(k => k.Key, v => v.Value);
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFilePath, json);
        }
        catch (Exception ex)
        {
            logger.Warning($"[RevivalMod.Server] Failed to save state file: {ex.Message}");
        }
    }

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

    public RevivalAuthorityResponse TryStartRevive(string playerId, string reviverId, string source)
    {
        var entry = GetOrCreate(playerId);
        lock (_sync)
        {
            var now = Now();

            if (entry.State == RevivalState.CoolDown && entry.CooldownUntilUnixSeconds > now)
            {
                return Denied("Player on cooldown", entry);
            }

            if (entry.State is RevivalState.Reviving or RevivalState.Revived)
            {
                return Denied($"Invalid state for revive start: {entry.State}", entry);
            }

            if (entry.State != RevivalState.BleedingOut)
            {
                return Denied($"Player is not downed: {entry.State}", entry);
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
                return Denied($"Cannot complete revive from {entry.State}", entry);
            }

            entry.State = RevivalState.Revived;
            entry.ReviverId = reviverId;
            entry.LastUpdatedUnixSeconds = Now();
        }
        Save();
        return Allowed(entry);
    }

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

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static RevivalAuthorityResponse Allowed(RevivalStateEntry state) =>
        new() { Success = true, State = state };

    private static RevivalAuthorityResponse Denied(string reason, RevivalStateEntry state) =>
        new() { Success = false, Reason = reason, State = state };
}
