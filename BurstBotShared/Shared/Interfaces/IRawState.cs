using System.Diagnostics.Contracts;
using DSharpPlus.Entities;

namespace BurstBotShared.Shared.Interfaces;

public interface IRawState<TState, TRaw, TProgress>
    where TState: IState<TState, TRaw, TProgress>
    where TRaw: IRawState<TState, TRaw, TProgress>
    where TProgress: Enum
{
#pragma warning disable CA2252
    [Pure]
    static abstract TRaw FromState(IState<TState, TRaw, TProgress> state);
#pragma warning restore CA2252
    Task<TState> ToState(DiscordGuild guild);
}