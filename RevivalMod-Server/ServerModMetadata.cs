using SPTarkov.Server.Core.Models.Spt.Mod;

namespace RevivalMod.Server;

/// <summary>
/// SPT 4 C# server mod metadata.
/// </summary>
public record ServerModMetadata : AbstractModMetadata
{
    public override string Name { get; init; } = "RevivalMod Server";
    public override string Author { get; init; } = "KaikiNoodles";
    public override List<string>? Contributors { get; init; } = [];
    public override List<string>? Incompatibilities { get; init; } = [];
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = [];
    public override string? Url { get; init; } = "https://github.com/KaikiNoodles/BringMeToLifeMod";
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
    public override string ModGuid { get; init; } = "RevivalMod";
    public override SemanticVersioning.Version Version { get; init; } = new(2, 0, 0);
    public override SemanticVersioning.Range SptVersion { get; init; } = new(">=4.0.12");
}
