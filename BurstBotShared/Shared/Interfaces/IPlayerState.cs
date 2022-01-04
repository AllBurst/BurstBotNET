using Remora.Discord.API.Abstractions.Objects;

namespace BurstBotShared.Shared.Interfaces;

public interface IPlayerState
{
    ulong PlayerId { get; init; }
    string PlayerName { get; set; }
    IChannel? TextChannel { get; set; }
    string AvatarUrl { get; set; }
}