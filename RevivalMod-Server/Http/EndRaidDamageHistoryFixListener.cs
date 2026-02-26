using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;

namespace RevivalMod.Server.Http;

/// <summary>
/// Intercepts POST /client/match/local/end to fix a vanilla EFT/SPT mismatch where the client
/// sends DamageStats.Type as an EDamageType integer but the SPT server expects a string.
/// Converts all integer Type values in DamageHistory to their EDamageType name before passing
/// the request to SptHttpListener.
/// </summary>
[Injectable(TypePriority = 1)]
public class EndRaidDamageHistoryFixListener(SptHttpListener sptHttpListener) : IHttpListener
{
    private const string TargetPath = "/client/match/local/end";

    // EFT.EDamageType — [Flags] enum
    private static readonly Dictionary<long, string> DamageTypeNames = new()
    {
        [1]        = "Undefined",
        [2]        = "Fall",
        [4]        = "Explosion",
        [8]        = "Barbed",
        [16]       = "Flame",
        [32]       = "GrenadeFragment",
        [64]       = "Impact",
        [128]      = "Existence",
        [256]      = "Medicine",
        [512]      = "Bullet",
        [1024]     = "Melee",
        [2048]     = "Landmine",
        [4096]     = "Sniper",
        [8192]     = "Blunt",
        [16384]    = "LightBleeding",
        [32768]    = "HeavyBleeding",
        [65536]    = "Dehydration",
        [131072]   = "Exhaustion",
        [262144]   = "RadExposure",
        [524288]   = "Stimulator",
        [1048576]  = "Poison",
        [2097152]  = "LethalToxin",
        [4194304]  = "Btr",
        [8388608]  = "Artillery",
        [16777216] = "HotGases",
        [33554432] = "ThermobaricExplosion",
        [67108864] = "Environment",
    };

    public bool CanHandle(MongoId sessionId, HttpContext context)
    {
        return context.Request.Method == "POST"
               && string.Equals(context.Request.Path.Value, TargetPath, StringComparison.OrdinalIgnoreCase);
    }

    public async Task Handle(MongoId sessionId, HttpContext context)
    {
        bool isCompressed = !context.Request.Headers.TryGetValue("requestcompressed", out var cv) || cv != "0";

        string body;
        if (isCompressed)
        {
            await using var zlibStream = new ZLibStream(context.Request.Body, CompressionMode.Decompress);
            using var reader = new StreamReader(zlibStream, Encoding.UTF8);
            body = await reader.ReadToEndAsync();
        }
        else
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            body = await reader.ReadToEndAsync();
        }

        var bodyBytes = Encoding.UTF8.GetBytes(FixDamageHistoryTypes(body));
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Headers["requestcompressed"] = "0";

        await sptHttpListener.Handle(sessionId, context);
    }

    private static string FixDamageHistoryTypes(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root is null) return json;

            var damageHistory = GetNode(root, "results", "profile", "Stats", "Eft", "DamageHistory");
            if (damageHistory is null) return json;

            if (GetProp(damageHistory, "BodyParts") is JsonObject bodyPartsObj)
                foreach (var (_, partNode) in bodyPartsObj)
                    if (partNode is JsonArray entries)
                        foreach (var entry in entries)
                            FixTypeField(entry as JsonObject);

            FixTypeField(GetProp(damageHistory, "LethalDamage") as JsonObject);

            return root.ToJsonString();
        }
        catch
        {
            return json;
        }
    }

    private static void FixTypeField(JsonObject? obj)
    {
        if (obj is null) return;
        var typeProp = GetProp(obj, "Type");
        if (typeProp is JsonValue typeVal && typeVal.TryGetValue<long>(out long intValue))
            obj["Type"] = JsonValue.Create(ResolveTypeName(intValue));
    }

    private static JsonNode? GetNode(JsonNode root, params string[] path)
    {
        JsonNode? current = root;
        foreach (var key in path)
        {
            current = GetProp(current, key);
            if (current is null) return null;
        }
        return current;
    }

    private static JsonNode? GetProp(JsonNode? node, string name)
    {
        if (node is not JsonObject obj) return null;
        if (obj[name] is { } exact) return exact;
        foreach (var kvp in obj)
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        return null;
    }

    private static string ResolveTypeName(long value)
    {
        if (DamageTypeNames.TryGetValue(value, out var name))
            return name;

        var parts = new List<string>();
        long remaining = value;
        foreach (var (flag, flagName) in DamageTypeNames.OrderByDescending(kv => kv.Key))
        {
            if (remaining == 0) break;
            if ((remaining & flag) == flag)
            {
                parts.Add(flagName);
                remaining &= ~flag;
            }
        }
        return parts.Count > 0 ? string.Join(", ", parts) : value.ToString();
    }
}
