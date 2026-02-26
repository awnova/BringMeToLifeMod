using RevivalMod.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;

namespace RevivalMod.Server.OnLoad;

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PreSptModLoader)]
public class RevivalPreLoad(ISptLogger<RevivalPreLoad> logger, RevivalConfigService configService,
    RevivalStateService stateService) : IOnLoad
{
    public async Task OnLoad()
    {
        await configService.OnPreLoadAsync();
        stateService.Load();
        logger.Info("[RevivalMod.Server] Pre-load complete.");
    }
}
