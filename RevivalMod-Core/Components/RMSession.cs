using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace RevivalMod.Components
{
    internal class RMSession : MonoBehaviour
    {
        private RMSession() { }
        private static RMSession _instance = null;

        public Player Player { get; private set; }
        public GameWorld GameWorld { get; private set; }
        public GamePlayerOwner GamePlayerOwner { get; private set; }

        // Dictionary to track all player states (single source of truth)
        public Dictionary<string, RMPlayer> PlayerStates = [];
        
        // Backward compatibility: critical players set for quick lookups
        public HashSet<string> CriticalPlayers = [];

        public static RMSession Instance
        {
            get
            {
                if (_instance is not null) 
                    return _instance;
                
                if (!Singleton<GameWorld>.Instantiated)
                {
                    Plugin.LogSource.LogError("Can't get ModSession Instance when GameWorld is not instantiated!");

                    // Create a temporary instance for error resistance
                    GameObject go = new("RMSessionTemp");
                    _instance = go.AddComponent<RMSession>();

                    return _instance;
                }

                try
                {
                    _instance = Singleton<GameWorld>.Instance.MainPlayer.gameObject.GetOrAddComponent<RMSession>();
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogError($"Error creating RMSession: {ex.Message}");
                    
                    GameObject go = new("RMSessionError");
                    
                    _instance = go.AddComponent<RMSession>();
                }
                
                return _instance;
            }
        }

        private void Awake()
        {
            try
            {
                if (!Singleton<GameWorld>.Instantiated) 
                    return;
                
                GameWorld = Singleton<GameWorld>.Instance;
                Player = GameWorld.MainPlayer;
                
                if (Player is not null)
                {
                    GamePlayerOwner = Player.gameObject.GetComponent<GamePlayerOwner>();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in RMSession.Awake: {ex.Message}");
            }
        }

        public static void AddToCriticalPlayers(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                Plugin.LogSource.LogError("Tried to add player with null or empty ID");
                return;
            }

            if (Singleton<GameWorld>.Instance.MainPlayer.ProfileId == playerId) 
                return;

            Instance.CriticalPlayers.Add(playerId);
            
            Plugin.LogSource.LogDebug($"Player {playerId} added to critical players.");
        }

        public static void RemovePlayerFromCriticalPlayers(string playerId)
        {
            if (string.IsNullOrEmpty(playerId) ||
                Singleton<GameWorld>.Instance.MainPlayer.ProfileId == playerId) 
                return;

            Instance.CriticalPlayers.Remove(playerId);
            
            Plugin.LogSource.LogDebug($"Player {playerId} removed from critical players.");
        }

        public static HashSet<string> GetCriticalPlayers()
        {
            return Instance.CriticalPlayers;
        }

        public static Dictionary<string, RMPlayer> GetPlayerStates()
        {
            return Instance.PlayerStates;
        }

        public static RMPlayer GetPlayerState(string playerId)
        {
            if (!Instance.PlayerStates.ContainsKey(playerId))
            {
                Instance.PlayerStates[playerId] = new RMPlayer();
            }
            return Instance.PlayerStates[playerId];
        }

        public static bool HasPlayerState(string playerId)
        {
            return Instance.PlayerStates.ContainsKey(playerId);
        }

        // Not sure yet
        public static int GetTotalPlayers()
        {
            return Singleton<GameWorld>.Instance.allObservedPlayersByID.Count;
        }

        public static List<Player> GetAllAlivePlayerList()
        {
            return Singleton<GameWorld>.Instance.AllAlivePlayersList;
        }
    }
}