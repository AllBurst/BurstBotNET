using BurstBotShared.Api;
using BurstBotShared.Services;
using BurstBotShared.Shared.Models.Game;
using BurstBotShared.Shared.Models.Localization;

namespace BurstBotShared.Shared.Models.Data;

public record State
{
    public GameStates GameStates { get; init; } = null!;
    public Localizations Localizations { get; init; } = null!;
    public BurstApi BurstApi { get; init; } = null!;
    public Config.Config Config { get; init; } = null!;

    public DeckService DeckService { get; init; } = null!;

    public AmqpService AmqpService { get; init; } = null!;
};