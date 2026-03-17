//====================[ Imports ]====================
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Utils;

namespace KeepMeAlive.Server.Services;

//====================[ RevivalConfigService ]====================
[Injectable(InjectionType.Singleton)]
public class RevivalConfigService(ModHelper modHelper, JsonUtil jsonUtil)
{
    //====================[ State ]====================
    public RevivalServerConfig Config { get; private set; } = new();

    public string ModPath => modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

    //====================[ Lifecycle ]====================
    public async Task OnPreLoadAsync()
    {
        var configPath = Path.Combine(ModPath, "config.json");
        if (File.Exists(configPath))
        {
            Config = await jsonUtil.DeserializeFromFileAsync<RevivalServerConfig>(configPath) ?? new RevivalServerConfig();
        }
        else
        {
            Config = new RevivalServerConfig();
        }

        // Ensure defaults are persisted when new fields are added.
        await File.WriteAllTextAsync(configPath, jsonUtil.Serialize(Config, true));
    }
}
