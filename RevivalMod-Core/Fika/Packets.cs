using LiteNetLib.Utils;
using System;
using UnityEngine;

namespace RevivalMod.Fika.Packets
{
    public struct ReviveStartedPacket : INetSerializable
    {
        public string reviveeId;
        public string reviverId;

        public void Deserialize(NetDataReader reader)
        {
            reviveeId = reader.GetString();
            reviverId = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(reviveeId);
            writer.Put(reviverId);
        }
    }

    public struct ReviveCanceledPacket : INetSerializable
    {
        public string reviveeId;
        public string reviverId;

        public void Deserialize(NetDataReader reader)
        {
            reviveeId = reader.GetString();
            reviverId = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(reviveeId);
            writer.Put(reviverId);
        }
    }

    public struct ReviveMePacket : INetSerializable
    {
        public string reviveeId;
        public string reviverId;

        public void Deserialize(NetDataReader reader)
        {
            reviveeId = reader.GetString();
            reviverId = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(reviveeId);
            writer.Put(reviverId);
        }
    }

    public struct RevivedPacket : INetSerializable
    {
        public string reviverId;

        public void Deserialize(NetDataReader reader)
        {
            reviverId = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(reviverId);
        }
    }

    public struct RemovePlayerFromCriticalPlayersListPacket : INetSerializable
    {
        public string playerId;
        public void Deserialize(NetDataReader reader)
        {
            playerId = reader.GetString();
        }
        public void Serialize(NetDataWriter writer) {
            writer.Put(playerId); 
        }
    }

    public struct PlayerCriticalStatePacket : INetSerializable
    {
        public string playerId;

        public void Deserialize(NetDataReader reader)
        {
            playerId = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(playerId);
        }
    }

    // Packet to request the host that runs AI to toggle ghost mode for a player
    public struct GhostModeTogglePacket : INetSerializable
    {
        public string playerId;
        public bool enterGhostMode;

        public void Deserialize(NetDataReader reader)
        {
            playerId = reader.GetString();
            enterGhostMode = reader.GetBool();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(playerId ?? string.Empty);
            writer.Put(enterGhostMode);
        }
    }

}
