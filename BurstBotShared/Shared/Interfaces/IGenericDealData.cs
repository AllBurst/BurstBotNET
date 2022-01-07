using BurstBotShared.Shared.Models.Game.Serializables;

namespace BurstBotShared.Shared.Interfaces;

public interface IGenericDealData
{
    ClientType? ClientType { get; init; }
    GameType GameType { get; }
    string GameId { get; init; }
    ulong PlayerId { get; init; }
    ulong ChannelId { get; init; }
    string? PlayerName { get; init; }
    string? AvatarUrl { get; init; }
}