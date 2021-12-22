using BurstBotShared.Shared.Models.Game.BlackJack.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using DSharpPlus.Entities;

namespace BurstBotShared.Shared.Models.Game.BlackJack;

public class BlackJackPlayerState
{
    public string GameId { get; set; } = "";
    public ulong PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public DiscordChannel? TextChannel { get; set; }
    public long OwnTips { get; set; }
    public int BetTips { get; set; }
    public int Order { get; set; }
    public List<Card> Cards { get; set; } = new();
    public string AvatarUrl { get; set; } = "";

    public static Task<BlackJackPlayerState> FromRaw(DiscordGuild guild, RawBlackJackPlayerState rawState)
        => rawState.ToPlayerState(guild);

    public RawBlackJackPlayerState ToRaw() => RawBlackJackPlayerState.FromPlayerState(this);
}