namespace BurstBotShared.Shared.Interfaces;

public interface IRawPlayerState
{
    string GameId { get; init; }
    ulong PlayerId { get; init; }
    string PlayerName { get; init; }
    ulong ChannelId { get; init; }
    string AvatarUrl { get; init; }
}