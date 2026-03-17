//====================[ Imports ]====================
using System.Text.Json;

namespace KeepMeAlive.Server;

//====================[ RevivalServerConfig ]====================
public sealed class RevivalServerConfig
{
    public RevivalItemConfig RevivalItem { get; set; } = new();

    //====================[ Load ]====================
    public static RevivalServerConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new RevivalServerConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RevivalServerConfig>(json) ?? new RevivalServerConfig();
    }
}

//====================[ RevivalItemConfig ]====================
public sealed class RevivalItemConfig
{
    public TradingConfig Trading { get; set; } = new();
}

//====================[ TradingConfig ]====================
public sealed class TradingConfig
{
    public string Trader { get; set; } = "Therapist";
    public int AmountRoubles { get; set; } = 200000;
}
