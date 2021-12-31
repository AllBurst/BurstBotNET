using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using DSharpPlus.Entities;
using Newtonsoft.Json;

namespace BurstBotShared.Shared.Models.Game.NinetyNine;

public class NinetyNinePlayerState : IState<NinetyNinePlayerState, RawNinetyNinePlayerState, NinetyNineGameProgress>
{
    public string GameId { get; set; } = null!;
    public ulong PlayerId { get; set; }
    public string PlayerName { get; set; } = null!;
    public DiscordChannel? TextChannel { get; set; }
    public int Order { get; set; }
    public ImmutableArray<Card> Cards { get; set; } = new();
    public string AvatarUrl { get; set; } = null!;
    
    public Channel<Tuple<ulong, byte[]>>? PayloadChannel => null;

    public NinetyNineGameProgress GameProgress
    {
        get => throw new InvalidOperationException(ErrorMessages.PlayerStateNoGameProgress);
        set => throw new InvalidOperationException(ErrorMessages.PlayerStateNoGameProgress);
    }
}