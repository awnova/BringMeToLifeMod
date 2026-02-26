using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Utils;

namespace RevivalMod.Server.Services;

[Injectable(InjectionType.Singleton)]
public class RevivalConfigService(ModHelper modHelper, JsonUtil jsonUtil)
{
    public RevivalServerConfig Config { get; private set; } = new();

    public string ModPath => modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

    public async Task OnPreLoadAsync()
    {
        var configPath = Path.Combine(ModPath, "config.json");
        Config = await jsonUtil.DeserializeFromFileAsync<RevivalServerConfig>(configPath) ?? new RevivalServerConfig();

        // Ensure defaults are persisted when new fields are added.
        await File.WriteAllTextAsync(configPath, jsonUtil.Serialize(Config, true));
    }
}
