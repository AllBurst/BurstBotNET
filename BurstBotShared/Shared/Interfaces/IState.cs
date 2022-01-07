using System.Threading.Channels;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Interfaces;

public interface IState<TState, out TRaw, TProgress>
    where TRaw : IRawState<TState, TRaw, TProgress>
    where TState : IState<TState, TRaw, TProgress>
    where TProgress: Enum
{
    static Task<TState> FromRaw(IDiscordRestGuildAPI guildApi, Snowflake guild, TRaw rawState)
        => rawState.ToState(guildApi, guild);

    TRaw ToRaw() => TRaw.FromState(this);
}