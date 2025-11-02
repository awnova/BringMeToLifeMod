//====================[ Imports ]====================
using LiteNetLib.Utils;

namespace RevivalMod.Fika.Packets
{
    //====================[ Revival Mod Packet System ]====================

    //====================[ BleedingOutPacket ]====================
    // Broadcast when a player enters downed/critical state.
    public struct BleedingOutPacket : INetSerializable
    {
        public string playerId;
        public float timeRemaining;

        public void Deserialize(NetDataReader reader)
        {
            playerId = reader.GetString();
            timeRemaining = reader.GetFloat();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(playerId ?? string.Empty);
            writer.Put(timeRemaining);
        }
    }

    //====================[ TeamHelpPacket ]====================
    // Sent when a teammate begins helping a downed player (hold begins).
    public struct TeamHelpPacket : INetSerializable
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
            writer.Put(reviveeId ?? string.Empty);
            writer.Put(reviverId ?? string.Empty);
        }
    }

    //====================[ TeamCancelPacket ]====================
    // Sent when the teammate releases early (hold cancelled).
    public struct TeamCancelPacket : INetSerializable
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
            writer.Put(reviveeId ?? string.Empty);
            writer.Put(reviverId ?? string.Empty);
        }
    }

    //====================[ SelfReviveStartPacket ]====================
    // Sent when self-revival animation begins.
    public struct SelfReviveStartPacket : INetSerializable
    {
        public string playerId;

        public void Deserialize(NetDataReader reader)
        {
            playerId = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(playerId ?? string.Empty);
        }
    }

    //====================[ TeamReviveStartPacket ]====================
    // Sent when team revival animation begins on revivee.
    public struct TeamReviveStartPacket : INetSerializable
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
            writer.Put(reviveeId ?? string.Empty);
            writer.Put(reviverId ?? string.Empty);
        }
    }

    //====================[ RevivedPacket ]====================
    // Sent when revival completes successfully (invulnerability begins).
    public struct RevivedPacket : INetSerializable
    {
        public string playerId;
        public string reviverId; // empty if self-revive

        public void Deserialize(NetDataReader reader)
        {
            playerId = reader.GetString();
            reviverId = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(playerId ?? string.Empty);
            writer.Put(reviverId ?? string.Empty);
        }
    }

    //====================[ PlayerStateResetPacket ]====================
    // Sent when invulnerability ends and player returns to normal.
    public struct PlayerStateResetPacket : INetSerializable
    {
        public string playerId;

        public void Deserialize(NetDataReader reader)
        {
            playerId = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(playerId ?? string.Empty);
        }
    }
}
