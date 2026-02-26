using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RevivalMod.Server.Models.Revival;
using RevivalMod.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;
using SPTarkov.Server.Core.Utils;

namespace RevivalMod.Server.Http;

/// <summary>
/// Handles RevivalMod state routes via raw HTTP, bypassing StaticRouter's IRequestData cast requirement.
/// </summary>
[Injectable(TypePriority = 0)]
public class RevivalStateHttpListener(RevivalStateService stateService, HttpResponseUtil httpResponseUtil) : IHttpListener
{
    private const string BasePath = "/kaikinoodles/revivalmod/state/";

    public bool CanHandle(MongoId sessionId, HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        return context.Request.Method == "POST" && path.StartsWith(BasePath, StringComparison.OrdinalIgnoreCase);
    }

    public async Task Handle(MongoId sessionId, HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var body = await ReadBodyAsync(context.Request);
        var info = ParseJson(body);

        string json;
        try
        {
            var response = Dispatch(path, info);
            json = httpResponseUtil.NoBody(response);
        }
        catch (Exception ex)
        {
            json = JsonSerializer.Serialize(new RevivalAuthorityResponse
            {
                Success = false,
                Reason = ex.Message
            });
        }

        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(json);
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or 0)
            return "{}";

        request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        return string.IsNullOrEmpty(body) ? "{}" : body;
    }

    private static Dictionary<string, JsonElement>? ParseJson(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var d = new Dictionary<string, JsonElement>();
            foreach (var p in root.EnumerateObject())
                d[p.Name] = p.Value.Clone();
            return d;
        }
        catch
        {
            return null;
        }
    }

    private static string GetStr(Dictionary<string, JsonElement>? d, string key)
    {
        if (d == null || !d.TryGetValue(key, out var v))
            return string.Empty;
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
    }

    private static float GetFloat(Dictionary<string, JsonElement>? d, string key)
    {
        if (d == null || !d.TryGetValue(key, out var v))
            return 0f;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f))
            return f;
        return float.TryParse(v.ToString(), out var parsed) ? parsed : 0f;
    }

    private object Dispatch(string path, Dictionary<string, JsonElement>? info)
    {
        if (path.EndsWith("begin-critical", StringComparison.OrdinalIgnoreCase))
        {
            var state = stateService.SetBleedingOut(GetStr(info, "PlayerId"));
            return new RevivalAuthorityResponse { Success = true, State = state };
        }

        if (path.EndsWith("request-revive-start", StringComparison.OrdinalIgnoreCase))
        {
            return stateService.TryStartRevive(
                GetStr(info, "PlayerId"),
                GetStr(info, "ReviverId"),
                GetStr(info, "Source"));
        }

        if (path.EndsWith("complete-revive", StringComparison.OrdinalIgnoreCase))
        {
            return stateService.TryCompleteRevive(GetStr(info, "PlayerId"), GetStr(info, "ReviverId"));
        }

        if (path.EndsWith("end-invulnerability", StringComparison.OrdinalIgnoreCase))
        {
            var state = stateService.MarkCooldown(GetStr(info, "PlayerId"), GetFloat(info, "DurationSeconds"));
            return new RevivalAuthorityResponse { Success = true, State = state };
        }

        if (path.EndsWith("reset", StringComparison.OrdinalIgnoreCase))
        {
            var state = stateService.Reset(GetStr(info, "PlayerId"));
            return new RevivalAuthorityResponse { Success = true, State = state };
        }

        if (path.EndsWith("get", StringComparison.OrdinalIgnoreCase))
        {
            var state = stateService.GetOrCreate(GetStr(info, "PlayerId"));
            return new RevivalAuthorityResponse { Success = true, State = state };
        }

        throw new InvalidOperationException($"Unknown route: {path}");
    }
}
