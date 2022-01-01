using System.Collections.Immutable;
using BurstBotShared.Shared.Models.Data;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Results;

namespace BurstBotShared.Shared.Interfaces;

public interface ISlashCommand
{
    static abstract string Name { get; }
    static abstract string Description { get; }
    static abstract ImmutableArray<IApplicationCommandOption> ApplicationCommandOptions { get; }
    static abstract Tuple<string, string, ImmutableArray<IApplicationCommandOption>> GetCommandTuple();

    Task<IResult> Handle();
}