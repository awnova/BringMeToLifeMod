//====================[ Imports ]====================
using KeepMeAlive.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;

namespace KeepMeAlive.Server.OnLoad;

//====================[ RevivalPostLoad ]====================
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class RevivalPostLoad(ISptLogger<RevivalPostLoad> logger, RevivalDatabasePatchService databasePatchService) : IOnLoad
{
    //====================[ Lifecycle ]====================
    public Task OnLoad()
    {
        databasePatchService.OnPostLoad();
        logger.Info("[KeepMeAlive.Server] Post-load complete.");
        return Task.CompletedTask;
    }
}
