using System.Collections.Immutable;
using System.Globalization;
using BurstBotShared.Shared.Models.Game.NinetyNine.Serializables;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Results;

namespace BurstBotNET.Commands.NinetyNine;

public partial class NinetyNine
{
    private static readonly TextInfo TextInfo = new CultureInfo("en-US", false).TextInfo;
    
    private async Task<IResult> Join(float baseBet, NinetyNineDifficulty difficulty, NinetyNineVariation variation, params IUser?[] users)
    {
        var mentionedPlayers = Game
            .BuildPlayerList(_context, users)
            .ToImmutableArray();

        var result = await _interactionApi
            .EditOriginalInteractionResponseAsync(_context.ApplicationID, _context.Token,
                $"Difficulty: {difficulty}\nVariation: {variation}\nInvited players: {string.Join(' ', mentionedPlayers.Select(p => $"<@!{p}>"))}");

        return !result.IsSuccess ? Result.FromError(result) : Result.FromSuccess();
    }
}