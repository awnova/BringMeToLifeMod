using RevivalMod.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace RevivalMod.Server.Callbacks;

[Injectable]
public class RevivalStateCallbacks(HttpResponseUtil httpResponseUtil)
{
    public ValueTask<string> DataToClient(string url, Dictionary<string, object> info, MongoId sessionId)
    {
        return new ValueTask<string>(httpResponseUtil.NoBody(new
        {
            status = "ok",
            message = "Server received data",
            data = info
        }));
    }

    public ValueTask<string> DataToServer(string url, Dictionary<string, object> info, MongoId sessionId)
    {
        return new ValueTask<string>(httpResponseUtil.NoBody(new
        {
            status = "ok",
            message = "Data received by server",
            data = info
        }));
    }
}
