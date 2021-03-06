using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BurstBotShared.Shared.Models.Config;

public record Config
{
    public string Token { get; init; } = "";
    public string LogLevel { get; init; } = "";
    public List<string> TestGuilds { get; init; } = new();
    public bool RecreateGlobals { get; init; }
    public bool RecreateGuilds { get; init; }
    public string ServerEndpoint { get; init; } = "";
    public string SocketEndpoint { get; init; } = "";
    public int SocketPort { get; init; }
    public long Timeout { get; init; }
    public string LotteryEndpoint { get; init; } = "";
    public string LotteryUsername { get; init; } = "";
    public string LotteryPassword { get; init; } = "";
    public RabbitMqConfig Rabbit { get; init; } = new();

    private const string ConfigDirectoryName = "Config";
    private const string ConfigFilePath = ConfigDirectoryName + "/config.yaml";
    private static readonly ISerializer ConfigSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer ConfigDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static Config? LoadConfig()
    {
        if (!Directory.Exists(ConfigDirectoryName))
        {
            Directory.CreateDirectory(ConfigDirectoryName);
        }

        try
        {
            return File.Exists(ConfigFilePath)
                ? ConfigDeserializer.Deserialize<Config>(File.ReadAllText(ConfigFilePath))
                : DefaultConfig();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to create/read config from local disk: {ex.Message}");
        }

        return null;
    }

    private static Config DefaultConfig()
    {
        var defaultConfig = new Config
        {
            Timeout = 60,
            LogLevel = "DEBUG"
        };
        
        File.WriteAllText(ConfigFilePath, ConfigSerializer.Serialize(defaultConfig));
        return defaultConfig;
    }
}