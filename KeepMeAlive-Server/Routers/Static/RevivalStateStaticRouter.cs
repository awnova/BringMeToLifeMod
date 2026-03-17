//====================[ Imports ]====================
using KeepMeAlive.Server.Callbacks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace KeepMeAlive.Server.Routers.Static;

//====================[ RevivalStateStaticRouter ]====================
[Injectable]
public class RevivalStateStaticRouter(RevivalStateCallbacks callbacks, JsonUtil jsonUtil) : StaticRouter(jsonUtil, [
        new RouteAction<Dictionary<string, object>>(
            "/kaikinoodles/keepmealive/data_to_client",
            async (url, info, sessionId, output) => await callbacks.DataToClient(url, info, sessionId)
        ),
        new RouteAction<Dictionary<string, object>>(
            "/kaikinoodles/keepmealive/data_to_server",
            async (url, info, sessionId, output) => await callbacks.DataToServer(url, info, sessionId)
        )
    ])
{
}
