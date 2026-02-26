using RevivalMod.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;

namespace RevivalMod.Server.OnLoad;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class RevivalPostLoad(ISptLogger<RevivalPostLoad> logger, RevivalDatabasePatchService databasePatchService) : IOnLoad
{
    public Task OnLoad()
    {
        databasePatchService.OnPostLoad();
        logger.Info("[RevivalMod.Server] Post-load complete.");
        return Task.CompletedTask;
    }
}
