//====================[ Imports ]====================
using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using SPT.Reflection.Utils; // GetOrAddComponent
using UnityEngine;

namespace KeepMeAlive.Components
{
    //====================[ RMSession ]====================
    [DisallowMultipleComponent]
    internal class RMSession : MonoBehaviour
    {
        public static event Action<string, RMState, RMState> PlayerStateChanged;

        //====================[ Singleton ]====================
        private static RMSession _instance;
        
        public static RMSession Instance
        {
            get
            {
                if (_instance != null) return _instance;

                if (!Singleton<GameWorld>.Instantiated)
                {
                    Plugin.LogSource.LogError("RMSession requested before GameWorld instantiated.");
                    var go = new GameObject("RMSessionTemp");
                    _instance = go.AddComponent<RMSession>();
                    return _instance;
                }

                try
                {
                    var main = Singleton<GameWorld>.Instance.MainPlayer;
                    _instance = main.gameObject.GetOrAddComponent<RMSession>();
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError($"Error creating RMSession: {ex.Message}");
                    var go = new GameObject("RMSessionError");
                    _instance = go.AddComponent<RMSession>();
                }

                return _instance;
            }
        }

        //====================[ World & Player ]====================
        public Player          Player          { get; private set; }
        public GameWorld       GameWorld       { get; private set; }
        public GamePlayerOwner GamePlayerOwner { get; private set; }

        //====================[ State Stores ]====================
        
        
        
        // Single source of truth for per-player revival state.
        private Dictionary<string, RMPlayer> PlayerStates = new();

        // Back-compat quick lookup for "critical" players.
        private HashSet<string> CriticalPlayers = new();

        //====================[ Unity Hooks ]====================
        private void Awake()
        {
            try
            {
                if (!Singleton<GameWorld>.Instantiated) return;

                GameWorld = Singleton<GameWorld>.Instance;
                Player    = GameWorld.MainPlayer;

                if (Player != null)
                {
                    GamePlayerOwner = Player.gameObject.GetComponent<GamePlayerOwner>();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"RMSession.Awake error: {ex.Message}");
            }
        }

        //====================[ Critical Players (Back-Compat) ]====================
        public static void AddToCriticalPlayers(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                Plugin.LogSource.LogError("AddToCriticalPlayers: null/empty id.");
                return;
            }

            // State is authoritative elsewhere; this set is a compatibility mirror.
            Instance.CriticalPlayers.Add(playerId);
            Helpers.RevivalDebugLog.LogDebug($"CriticalPlayers: added {playerId}");
        }

        public static void RemovePlayerFromCriticalPlayers(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return;

            Instance.CriticalPlayers.Remove(playerId);
            Helpers.RevivalDebugLog.LogDebug($"CriticalPlayers: removed {playerId}");
        }

        // Check using PlayerStates (authoritative) instead of the back-compat set.
        public static bool IsPlayerCritical(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return false;
            
            return HasPlayerState(playerId) && GetPlayerState(playerId).IsCritical;
        }

        //====================[ State Accessors ]====================
        public static Dictionary<string, RMPlayer> GetPlayerStates() => Instance.PlayerStates;

        public static RMPlayer GetPlayerState(string playerId)
        {
            if (!Instance.PlayerStates.TryGetValue(playerId, out var state))
            {
                state = new RMPlayer();
                Instance.PlayerStates[playerId] = state;
            }
            return state;
        }

        public static bool HasPlayerState(string playerId) => Instance.PlayerStates.ContainsKey(playerId);

        public static bool SetPlayerState(string playerId, RMState newState)
        {
            if (string.IsNullOrEmpty(playerId)) return false;

            var state = GetPlayerState(playerId);
            var oldState = state.State;
            if (oldState == newState) return false;

            state.State = newState;

            try
            {
                PlayerStateChanged?.Invoke(playerId, oldState, newState);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"RMSession.SetPlayerState event error: {ex.Message}");
            }

            return true;
        }

        //====================[ Partial Update (Unified Surv Flow) ]====================
        // Apply selective updates from a source snapshot to the live session state.
        public static void UpdatePlayerState(string playerId, RMPlayer source)
        {
            if (!HasPlayerState(playerId)) return;

            var live = GetPlayerState(playerId);
            if (live == null) return;

            // Copy the fields needed by the unified Surv flow.
            // State is included so snapshot-based callers (not just live-ref callers) commit correctly.
            SetPlayerState(playerId, source.State);
            live.ReviveRequestedSource = source.ReviveRequestedSource;
            live.CurrentReviverId      = source.CurrentReviverId;
        }

        // Note: For general player lookups, use Utils.GetPlayerById() / Utils.GetPlayerDisplayName()
    }
}