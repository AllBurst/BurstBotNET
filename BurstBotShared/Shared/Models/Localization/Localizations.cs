using BurstBotShared.Shared.Models.Localization.Serializables;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BurstBotShared.Shared.Models.Localization;

public class Localizations
{
    private const string LocalizationPath = "Assets/localization/localizations.yaml";
    private readonly Localization _japanese;
    private readonly Localization _english;
    private readonly Localization _chinese;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public Localizations()
    {
        var rawText = Deserializer.Deserialize<RawLocalizations>(File.ReadAllText(LocalizationPath));
        var jpRaw = Deserializer.Deserialize<RawLocalization>(File.ReadAllText(rawText.Japanese));
        _japanese = Localization.FromRaw(jpRaw);
        
        var enRaw = Deserializer.Deserialize<RawLocalization>(File.ReadAllText(rawText.English));
        _english = Localization.FromRaw(enRaw);
        
        var cnRaw = Deserializer.Deserialize<RawLocalization>(File.ReadAllText(rawText.Chinese));
        _chinese = Localization.FromRaw(cnRaw);
    }

    public Localization GetLocalization(Language language = Language.English)
        => language switch
        {
            Language.Japanese => _japanese,
            Language.English => _english,
            Language.Chinese => _chinese,
            _ => _english
        };
}