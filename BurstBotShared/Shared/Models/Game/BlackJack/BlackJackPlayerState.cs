using System.Collections.Immutable;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Remora.Discord.API.Abstractions.Objects;

namespace BurstBotShared.Shared.Models.Game.BlackJack;

public class BlackJackPlayerState : IState<BlackJackPlayerState, RawBlackJackPlayerState, BlackJackGameProgress>
{
    public string GameId { get; set; } = "";
    public ulong PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public IChannel? TextChannel { get; set; }
    public long OwnTips { get; set; }
    public int BetTips { get; set; }
    public int Order { get; set; }
    public ImmutableArray<Card> Cards { get; set; }
    public string AvatarUrl { get; set; } = "";

    public Channel<Tuple<ulong, byte[]>>? PayloadChannel => null;

    public BlackJackGameProgress GameProgress
    {
        get => throw new InvalidOperationException("Player state doesn't have game progress.");
        set => throw new InvalidOperationException("Player state doesn't have game progress.");
    }
}