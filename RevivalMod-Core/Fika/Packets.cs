using Fika.Core.Networking.LiteNetLib.Utils;

namespace RevivalMod.Fika.Packets
{
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

    public struct TeamHelpPacket : INetSerializable
    {
        public string reviveeId;
        public string reviverId;

        public void Deserialize(NetDataReader reader) { reviveeId = reader.GetString(); reviverId = reader.GetString(); }
        public void Serialize(NetDataWriter writer) { writer.Put(reviveeId ?? ""); writer.Put(reviverId ?? ""); }
    }

    public struct TeamCancelPacket : INetSerializable
    {
        public string reviveeId;
        public string reviverId;

        public void Deserialize(NetDataReader reader) { reviveeId = reader.GetString(); reviverId = reader.GetString(); }
        public void Serialize(NetDataWriter writer) { writer.Put(reviveeId ?? ""); writer.Put(reviverId ?? ""); }
    }

    public struct SelfReviveStartPacket : INetSerializable
    {
        public string playerId;

        public void Deserialize(NetDataReader reader) { playerId = reader.GetString(); }
        public void Serialize(NetDataWriter writer) { writer.Put(playerId ?? ""); }
    }

    public struct TeamReviveStartPacket : INetSerializable
    {
        public string reviveeId;
        public string reviverId;

        public void Deserialize(NetDataReader reader) { reviveeId = reader.GetString(); reviverId = reader.GetString(); }
        public void Serialize(NetDataWriter writer) { writer.Put(reviveeId ?? ""); writer.Put(reviverId ?? ""); }
    }

    public struct RevivedPacket : INetSerializable
    {
        public string playerId;
        public string reviverId;

        public void Deserialize(NetDataReader reader) { playerId = reader.GetString(); reviverId = reader.GetString(); }
        public void Serialize(NetDataWriter writer) { writer.Put(playerId ?? ""); writer.Put(reviverId ?? ""); }
    }

    public struct PlayerStateResetPacket : INetSerializable
    {
        public string playerId;
        public bool isDead;
        public float cooldownSeconds;

        public void Deserialize(NetDataReader reader) { playerId = reader.GetString(); isDead = reader.GetBool(); cooldownSeconds = reader.GetFloat(); }
        public void Serialize(NetDataWriter writer) { writer.Put(playerId ?? ""); writer.Put(isDead); writer.Put(cooldownSeconds); }
    }

    public struct TeamHealPacket : INetSerializable
    {
        public string patientId;
        public string healerId;

        public void Deserialize(NetDataReader reader) { patientId = reader.GetString(); healerId = reader.GetString(); }
        public void Serialize(NetDataWriter writer) { writer.Put(patientId ?? ""); writer.Put(healerId ?? ""); }
    }

    public struct TeamHealCompletePacket : INetSerializable
    {
        public string patientId;
        public string healerId;

        public void Deserialize(NetDataReader reader) { patientId = reader.GetString(); healerId = reader.GetString(); }
        public void Serialize(NetDataWriter writer) { writer.Put(patientId ?? ""); writer.Put(healerId ?? ""); }
    }

    public struct TeamHealCancelPacket : INetSerializable
    {
        public string patientId;
        public string healerId;

        public void Deserialize(NetDataReader reader) { patientId = reader.GetString(); healerId = reader.GetString(); }
        public void Serialize(NetDataWriter writer) { writer.Put(patientId ?? ""); writer.Put(healerId ?? ""); }
    }

    /// <summary>
    /// Periodic state heartbeat broadcast by every player in a non-None state.
    /// Sent every ~5 seconds and immediately on state transitions.
    /// Allows late-joining clients and clients that missed earlier packets to catch up.
    /// </summary>
    public struct PlayerStateResyncPacket : INetSerializable
    {
        public string playerId;
        public int    state;           // RMState cast to int
        public float  criticalTimer;
        public float  invulTimer;
        public float  cooldownTimer;
        public string reviverId;

        public void Deserialize(NetDataReader reader)
        {
            playerId      = reader.GetString();
            state         = reader.GetInt();
            criticalTimer = reader.GetFloat();
            invulTimer    = reader.GetFloat();
            cooldownTimer = reader.GetFloat();
            reviverId     = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(playerId      ?? "");
            writer.Put(state);
            writer.Put(criticalTimer);
            writer.Put(invulTimer);
            writer.Put(cooldownTimer);
            writer.Put(reviverId     ?? "");
        }
    }
}
