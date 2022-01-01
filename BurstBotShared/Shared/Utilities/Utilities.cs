using System.Collections.Immutable;
using System.Text;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Models.Data;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.Serializables;
using Microsoft.Extensions.Logging;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Commands.Contexts;
using Remora.Rest.Core;

namespace BurstBotShared.Shared.Utilities;

public static class Utilities
{
    private static readonly ImmutableArray<char> RandomCharacters =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".ToCharArray().ToImmutableArray();
    
    public static Embed BuildGameEmbed(IGuildMember invokingMember, IUser botUser,
        GenericJoinStatus joinStatus, string gameName, string description, int? secondsLeft)
    {
        var playerIds = joinStatus.PlayerIds
            .Select(id => $"💠<@!{id}>")
            .ToImmutableArray();
        var actualDescription = "Joined players: \n" + string.Join('\n', playerIds) + description;
        var displayName = invokingMember.GetDisplayName();
        var title = joinStatus.StatusType switch
        {
            GenericJoinStatusType.Start => $"{displayName} has started a {gameName} game!",
            GenericJoinStatusType.Matched =>
                $"{displayName}, you have successfully joined a {gameName} game!",
            _ => ""
        };

        return new Embed(
            Author: new EmbedAuthor(displayName, IconUrl: invokingMember.GetAvatarUrl()),
            Title: title,
            Colour: BurstColor.Burst.ToColor(),
            Thumbnail: new EmbedThumbnail(botUser.GetAvatarUrl()),
            Description: actualDescription,
            Footer: new EmbedFooter(joinStatus.StatusType == GenericJoinStatusType.Start
                ? $"React below to confirm to join! {secondsLeft ?? 30} seconds left!"
                : "Starting game..."));
    }

    public static string GenerateRandomString(int length = 30)
    {
        var builder = new StringBuilder();
        var random = new Random();
        for (var i = 0; i < length; i++)
            builder.Append(RandomCharacters[random.Next(0, RandomCharacters.Length)]);
        return builder.ToString();
    }

    public static async Task<IUser?> GetBotUser(IDiscordRestUserAPI userApi, ILogger logger)
    {
        var result = await userApi.GetCurrentUserAsync();
        if (result.IsSuccess) return result.Entity;
        
        logger.LogError("Failed to get bot user: {Reason}, inner: {Inner}",
            result.Error.Message, result.Inner);
        return null;
    }

    public static async Task<IGuildMember?> GetUserMember(InteractionContext context,
        IDiscordRestInteractionAPI interactionApi, string message, ILogger logger)
    {
        if (context.Member.IsDefined(out var member)) return member;
        
        logger.LogError("Failed to get member for slash command");
        var failureMessage = await interactionApi
            .EditOriginalInteractionResponseAsync(
                context.ApplicationID,
                context.Token,
                message);

        if (!failureMessage.IsSuccess)
        {
            logger.LogError("Failed to reply with the failure message: {Reason}, inner: {Inner}",
                failureMessage.Error.Message, failureMessage.Inner);
        }
        
        return null;
    }

    public static async Task<Snowflake?> GetGuildFromContext(
        InteractionContext context,
        IDiscordRestInteractionAPI interactionApi,
        ILogger logger)
    {
        var guildResult = context.GuildID.IsDefined(out var guild);
        if (!guildResult)
        {
            var errorMessageResult = await interactionApi
                .EditOriginalInteractionResponseAsync(
                    context.ApplicationID,
                    context.Token,
                    ErrorMessages.JoinNotInGuild);
            if (!errorMessageResult.IsSuccess)
                logger.LogError("Failed to edit original response: {Reason}, inner: {Inner}",
                    errorMessageResult.Error.Message, errorMessageResult.Inner);

            return null;
        }

        return guild;
    }

    public static ChinesePokerGameState GetChinesePokerGameState(
        IUser user,
        State state,
        InteractionContext context,
        out ChinesePokerPlayerState? playerState)
    {
        ChinesePokerPlayerState? pState = null;
        var (_, gameState) = state.GameStates.ChinesePokerGameStates.Item1
            .First(item =>
            {
                var (_, v) = item;
                var hasPlayer = v.Players.ContainsKey(user.ID.Value);
                var matchedPlayer = v.Players.Values
                    .Where(p => p.TextChannel!.ID.Equals(context.ChannelID) && p.PlayerId.Equals(user.ID.Value))
                    .ToImmutableList();
                var hasChannel = state.GameStates.ChinesePokerGameStates.Item2.Contains(context.ChannelID);
                if (!hasPlayer || matchedPlayer.IsEmpty || !hasChannel) return false;
                pState = matchedPlayer.First();
                return true;
            });
        playerState = pState;
        return gameState;
    }
}