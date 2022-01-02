using Remora.Discord.API.Abstractions.Objects;

namespace BurstBotShared.Shared.Interfaces;

public interface IPlayerState
{
    ulong PlayerId { get; init; }
    IChannel? TextChannel { get; set; }
}