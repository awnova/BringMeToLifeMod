using System.Text.Json;

namespace RevivalMod.Server;

public sealed class RevivalServerConfig
{
    public RevivalItemConfig RevivalItem { get; set; } = new();

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

public sealed class RevivalItemConfig
{
    public TradingConfig Trading { get; set; } = new();
}

public sealed class TradingConfig
{
    public string Trader { get; set; } = "Therapist";
    public int AmountRoubles { get; set; } = 150000;
}
