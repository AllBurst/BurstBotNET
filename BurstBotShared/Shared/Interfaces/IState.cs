using System.Threading.Channels;
using DSharpPlus.Entities;

namespace BurstBotShared.Shared.Interfaces;

public interface IState<TState, out TRaw, TProgress>
    where TRaw : IRawState<TState, TRaw, TProgress> 
    where TState : IState<TState, TRaw, TProgress>
    where TProgress: Enum
{
    static Task<TState> FromRaw(DiscordGuild guild, TRaw rawState)
        => rawState.ToState(guild);

    TRaw ToRaw() => TRaw.FromState(this);
    
    Channel<Tuple<ulong, byte[]>>? PayloadChannel { get; }
    TProgress? GameProgress { get; set; }
}