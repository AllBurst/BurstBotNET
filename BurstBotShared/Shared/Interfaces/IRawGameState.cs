namespace BurstBotShared.Shared.Interfaces;

public interface IRawGameState<TRawPlayerState, TProgress>
    where TRawPlayerState: IRawPlayerState
    where TProgress: Enum
{
    string GameId { get; init; }
    string LastActiveTime { get; init; }
    Dictionary<ulong, TRawPlayerState> Players { get; init; }
    TProgress Progress { get; init; }
    float BaseBet { get; init; }
}