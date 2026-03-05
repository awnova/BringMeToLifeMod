//====================[ Imports ]====================
using System;
using System.Threading.Tasks;
using EFT;

namespace KeepMeAlive.Helpers
{
    //====================[ RevivalAuthority ]====================
    internal static class RevivalAuthority
    {
        //====================[ Constants & Fields ]====================
        private const string BaseRoute = "/kaikinoodles/revivalmod/state";

        //====================[ Network Models ]====================
        private sealed class AuthorityRequest
        {
            public string PlayerId { get; set; } = string.Empty;
            public string ReviverId { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
            public float DurationSeconds { get; set; }
        }

        private sealed class AuthorityResponse
        {
            public bool Success { get; set; }
            public string Reason { get; set; } = string.Empty;
        }

        //====================[ Public API ]====================
        // Fire-and-forget: no response is consumed, so we offload to a background thread
        // to avoid blocking the Unity main thread (which causes a visible stutter for all clients).
        public static void NotifyBeginCritical(string playerId) =>
            Task.Run(() => Send($"{BaseRoute}/begin-critical", new AuthorityRequest { PlayerId = playerId }));

        public static bool TryAuthorizeReviveStart(string playerId, string reviverId, string source, out string reason)
        {
            var ok = Send($"{BaseRoute}/request-revive-start", new AuthorityRequest
            {
                PlayerId = playerId,
                ReviverId = reviverId,
                Source = source
            }, out var response);

            reason = ok ? (response?.Reason ?? string.Empty) : string.Empty;
            return ok ? (response?.Success ?? true) : true;
        }

        public static void NotifyReviveComplete(string playerId, string reviverId) =>
            Task.Run(() => Send($"{BaseRoute}/complete-revive", new AuthorityRequest { PlayerId = playerId, ReviverId = reviverId }));

        public static void NotifyEndInvulnerability(string playerId, float cooldownSeconds) =>
            Task.Run(() => Send($"{BaseRoute}/end-invulnerability", new AuthorityRequest { PlayerId = playerId, DurationSeconds = cooldownSeconds }));

        public static void NotifyReset(string playerId) =>
            Task.Run(() => Send($"{BaseRoute}/reset", new AuthorityRequest { PlayerId = playerId }));

        //====================[ Private Send Helpers ]====================
        private static bool Send(string route, object data) => Send(route, data, out _);

        private static bool Send(string route, object data, out AuthorityResponse response)
        {
            response = null;
            
            try
            {
                response = Utils.ServerRoute<AuthorityResponse>(route, data);
                return response != null;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug($"[RevivalAuthority] Route {route} unavailable: {ex.Message}");
                return false;
            }
        }
    }
}