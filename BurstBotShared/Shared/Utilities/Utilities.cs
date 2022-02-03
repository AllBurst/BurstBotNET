using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.Serializables;
using ConcurrentCollections;
using Microsoft.Extensions.Logging;
using OneOf;
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
        if (guildResult) return guild;
        
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

    public static TGameState GetGameState<TGameState, TPlayerState, TProgress>(
        IUser user,
        ConcurrentDictionary<string, TGameState> states,
        ConcurrentHashSet<Snowflake> channels,
        InteractionContext context,
        out TPlayerState? playerState)
        where TGameState: IGameState<TPlayerState, TProgress>
        where TPlayerState: class, IPlayerState
        where TProgress: Enum
    {
        TPlayerState? pState = null;
        var (_, gameState) = states
            .First(item =>
            {
                var (_, v) = item;
                var hasPlayer = v.Players.ContainsKey(user.ID.Value);
                var (_, channelId, _) = context;
                var matchedPlayer = v.Players.Values
                    .Where(p =>
                    {
                        var playerChannelId = p.TextChannel?.ID.Value ?? 0;
                        var channelMatched = playerChannelId != 0 && playerChannelId == channelId.Value;
                        return channelMatched && p.PlayerId.Equals(user.ID.Value);
                    })
                    .ToImmutableList();
                var hasChannel = channels.Contains(channelId);
                if (!hasPlayer || matchedPlayer.IsEmpty || !hasChannel) return false;
                pState = matchedPlayer.First();
                return true;
            });
        playerState = pState;
        return gameState;
    }

    public static bool HandleException(Exception exception, string messageContent, SemaphoreSlim semaphore, ILogger logger)
    {
        logger.LogError("An exception occurred when handling progress/ending result: {Exception}", exception);
        logger.LogError("Exception message: {Message}", exception.Message);
        logger.LogError("Source: {Source}", exception.Source);
        logger.LogError("Stack trace: {Trace}", exception.StackTrace);
        logger.LogError("Message content: {Content}", messageContent);
        
        if (semaphore.CurrentCount <= 0) return false;
        
        semaphore.Release();
        logger.LogDebug("Semaphore released in an exception");
        return false;
    }

    public static async Task<IChannel?> TryGetTextChannel(
        IEnumerable<Snowflake> guilds,
        ulong channelId,
        IDiscordRestGuildAPI guildApi)
    {
        foreach (var guild in guilds)
        {
            var getChannelsResult = await guildApi
                .GetGuildChannelsAsync(guild);
            if (!getChannelsResult.IsSuccess) continue;
            var channels = getChannelsResult.Entity;
            if (!channels.Any()) continue;
            var textChannel = channels
                .FirstOrDefault(c => c.ID.Value == channelId);
            if (textChannel == null) continue;
            return textChannel;
        }

        return null;
    }
    
    public static Card ExtractCard(IEnumerable<string> values)
    {
        var selection = values.FirstOrDefault()!;
        var suit = selection[..1];
        var rank = selection[1..];
        var playedCard = Card.Create(suit, rank);
        return playedCard;
    }

    public static async Task DisableComponents(
        IMessage message,
        bool disableAll,
        IDiscordRestChannelAPI channelApi,
        ILogger logger,
        CancellationToken ct,
        IEnumerable<string>? customIds = null)
    {
        var originalEmbeds = message.Embeds;
        var originalAttachments = message.Attachments
            .Select(OneOf<FileData, IPartialAttachment>.FromT1);
        var originalComponents = message.Components.Disable(disableAll, customIds);
        var editResult = await channelApi
            .EditMessageAsync(message.ChannelID, message.ID,
                embeds: originalEmbeds.ToImmutableArray(),
                attachments: originalAttachments.ToImmutableArray(),
                components: originalComponents,
                ct: ct);
        if (!editResult.IsSuccess)
            logger.LogError(
                "Failed to remove components from the original message: {Reason}, inner: {Inner}",
                editResult.Error.Message, editResult.Inner);
    }

    public static async Task<OneOf<FileData, IPartialAttachment>[]> BuildRepeatedAttachment(
        Stream originalImage,
        string fileName)
    {
        await using var imageCopy = new MemoryStream((int)originalImage.Length);
        await originalImage.CopyToAsync(imageCopy);
        originalImage.Seek(0, SeekOrigin.Begin);
        imageCopy.Seek(0, SeekOrigin.Begin);

        var attachments = new OneOf<FileData, IPartialAttachment>[]
        {
            new FileData(fileName, imageCopy)
        };

        return attachments;
    }
}