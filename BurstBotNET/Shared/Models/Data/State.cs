using BurstBotNET.Api;
using BurstBotNET.Services;
using BurstBotNET.Shared.Models.Game;
using BurstBotNET.Shared.Models.Localization;

namespace BurstBotNET.Shared.Models.Data;

public record State
{
    public GameStates GameStates { get; init; } = null!;
    public Localizations Localizations { get; init; } = null!;
    public BurstApi BurstApi { get; init; } = null!;
    public Config.Config Config { get; init; } = null!;

    public DeckService DeckService { get; init; } = null!;
};