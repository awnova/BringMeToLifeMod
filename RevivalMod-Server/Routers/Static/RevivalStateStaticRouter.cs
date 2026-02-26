using RevivalMod.Server.Callbacks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace RevivalMod.Server.Routers.Static;

[Injectable]
public class RevivalStateStaticRouter(RevivalStateCallbacks callbacks, JsonUtil jsonUtil) : StaticRouter(jsonUtil, [
        new RouteAction<Dictionary<string, object>>(
            "/kaikinoodles/revivalmod/data_to_client",
            async (url, info, sessionId, output) => await callbacks.DataToClient(url, info, sessionId)
        ),
        new RouteAction<Dictionary<string, object>>(
            "/kaikinoodles/revivalmod/data_to_server",
            async (url, info, sessionId, output) => await callbacks.DataToServer(url, info, sessionId)
        )
    ])
{
}
