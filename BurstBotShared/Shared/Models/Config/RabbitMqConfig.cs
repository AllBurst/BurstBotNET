namespace BurstBotShared.Shared.Models.Config;

public record RabbitMqConfig
{
    public string Endpoint { get; init; } = "";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
};