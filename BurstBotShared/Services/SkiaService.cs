using System.Collections.Immutable;
using BurstBotShared.Shared.Extensions;
using BurstBotShared.Shared.Interfaces;
using BurstBotShared.Shared.Models.Game.ChinesePoker;
using BurstBotShared.Shared.Models.Game.ChinesePoker.Serializables;
using BurstBotShared.Shared.Models.Game.Serializables;
using Flurl.Http;
using SkiaSharp;

namespace BurstBotShared.Services;

public static class SkiaService
{
    private const int MaxWidth = 2048;
    private const int Quality = 95;
    private const int Padding = 50;
    
    /// <summary>
    /// Render a full deck of cards horizontally.
    /// </summary>
    /// <param name="deck">The deck service that contains bitmaps of cards.</param>
    /// <param name="cards">A deck for which to render images.</param>
    /// <returns>The rendered image.</returns>
    public static Stream RenderDeck(DeckService deck, IEnumerable<Card> cards)
    {
        var bitmaps = cards
            .Select(deck.GetBitmap)
            .ToImmutableArray();
        
        var totalWidth = bitmaps
            .Select(bitmap => bitmap.Width)
            .Intersperse(Padding)
            .Sum();

        var height = bitmaps.Max(bitmap => bitmap.Height) + 2 * Padding;
        var scaleRatio = (float) totalWidth / MaxWidth;
        var ratio = 1.0f / scaleRatio;
        var actualWidth = (int)MathF.Floor(totalWidth * ratio);
        var actualHeight = (int)MathF.Floor(height * ratio);
        var surface = SKSurface.Create(new SKImageInfo(actualWidth, actualHeight));
        var canvas = surface.Canvas;

        var incrementalPadding = Padding * ratio;
        var currentX = incrementalPadding;
        var singleCardWidth = (int)MathF.Floor(bitmaps[0].Width * ratio);
        var singleCardHeight = (int)MathF.Floor(bitmaps[0].Height * ratio);
        var imageInfo = new SKImageInfo(singleCardWidth, singleCardHeight);
        foreach (var bitmap in bitmaps)
        {
            var scaledBitmap = new SKBitmap(imageInfo);
            bitmap.ScalePixels(scaledBitmap, SKFilterQuality.High);
            canvas.DrawBitmap(scaledBitmap, currentX, incrementalPadding);
            currentX += singleCardWidth + incrementalPadding;
        }
        
        var stream = new MemoryStream();
        surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, Quality).SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    /// <summary>
    /// Render a single card.
    /// </summary>
    /// <param name="deck">The deck service that contains bitmaps of cards.</param>
    /// <param name="card">A single card to render image for.</param>
    /// <returns>The rendered image.</returns>
    public static Stream RenderCard(DeckService deck, Card card)
    {
        var stream = new MemoryStream();
        deck.GetBitmap(card).Encode(SKEncodedImageFormat.Jpeg, Quality).SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    /// <summary>
    /// Render a full deck of Chinese Poker game as 3 horizontal rows. Each row has 3, 5 and 5 cards, respectively.
    /// </summary>
    /// <param name="deck">The deck service that contains bitmaps of cards.</param>
    /// <param name="cards">A deck for which to render images.</param>
    /// <returns>The rendered image.</returns>
    public static Stream RenderChinesePokerDeck(DeckService deck, IEnumerable<Card> cards)
    {
        var bitmaps = cards
            .Select(deck.GetBitmap)
            .ToImmutableList();

        var width = bitmaps[0].Width * 5 + 6 * Padding;
        var height = bitmaps[0].Height * 3 + 4 * Padding;
        var scaleRatio = (float) width / MaxWidth;
        var ratio = 1.0f / scaleRatio;
        var actualWidth = (int)MathF.Floor(width * ratio);
        var actualHeight = (int)MathF.Floor(height * ratio);
        var surface = SKSurface.Create(new SKImageInfo(actualWidth, actualHeight));
        var canvas = surface.Canvas;

        var incrementPadding = Padding * ratio;
        var currentX = incrementPadding;
        var currentY = incrementPadding;
        var singleCardWidth = (int)MathF.Floor(bitmaps[0].Width * ratio);
        var singleCardHeight = (int)MathF.Floor(bitmaps[0].Height * ratio);
        var imageInfo = new SKImageInfo(singleCardWidth, singleCardHeight);
        var indices = Enumerable.Range(1, 13);
        var indexedBitmaps = bitmaps.Zip(indices);
        foreach (var (bitmap, index) in indexedBitmaps)
        {
            var scaledBitmap = new SKBitmap(imageInfo);
            bitmap.ScalePixels(scaledBitmap, SKFilterQuality.High);
            canvas.DrawBitmap(scaledBitmap, currentX, currentY);
            currentX += singleCardWidth + incrementPadding;
            if (index is not (3 or 8)) continue;
            currentX = incrementPadding;
            currentY += singleCardHeight + incrementPadding;
        }

        var stream = new MemoryStream();
        surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, Quality).SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    /// <summary>
    /// Render the hand of Chinese Poker players based on the progress (Front Hand, Middle Hand, or Back Hand).
    /// </summary>
    /// <param name="playerStates">Chinese Poker players whose hands will be rendered and combined as a single image.</param>
    /// <param name="progress">The hand to render for.</param>
    /// <param name="deck">The deck service that contains bitmaps of cards.</param>
    /// <returns>The rendered image.</returns>
    public static async Task<Stream> RenderChinesePokerHand(
        IEnumerable<ChinesePokerPlayerState> playerStates,
        ChinesePokerGameProgress progress,
        DeckService deck)
    {
        var bitmapTasks = playerStates
            .Select(async p =>
            {
                var avatar = await p.AvatarUrl.GetStreamAsync();
                var avatarBitmap = SKBitmap.Decode(avatar);
                return (avatarBitmap, SKBitmap.Decode(p.DeckImages[progress]));
            })
            .ToImmutableList();
        var bitmaps = await Task.WhenAll(bitmapTasks);

        var totalCardWidth = bitmaps[0].Item2.Width;
        var cardHeight = bitmaps[0].Item2.Height;
        var avatarRatio = (float)bitmaps[0].avatarBitmap.Height / cardHeight;
        var avatarWidth = bitmaps[0].avatarBitmap.Width / avatarRatio;
        var avatarHeight = bitmaps[0].avatarBitmap.Height / avatarRatio;
        var width = Padding + avatarWidth + totalCardWidth + Padding;
        var height = cardHeight * 4.0f;

        var scaleRatio = width / MaxWidth;
        var ratio = 1.0f / scaleRatio;
        var actualWidth = (int)MathF.Floor(width * ratio);
        var actualHeight = (int)MathF.Floor(height * ratio);
        var surface = SKSurface.Create(new SKImageInfo(actualWidth, actualHeight));
        var canvas = surface.Canvas;

        var incrementalPadding = Padding * ratio;
        var currentX = incrementalPadding;
        var currentY = 0.0f;
        var cardsWidth = (int)MathF.Floor(totalCardWidth * ratio);
        var singleCardHeight = (int)MathF.Floor(cardHeight * ratio);
        var cardImageInfo = new SKImageInfo(cardsWidth, singleCardHeight);
        foreach (var (avatar, cards) in bitmaps)
        {
            var avatarImageInfo = new SKImageInfo((int)MathF.Floor(avatarWidth * ratio), (int)MathF.Floor(avatarHeight * ratio));
            var avatarBitmap = new SKBitmap(avatarImageInfo);
            avatar.ScalePixels(avatarBitmap, SKFilterQuality.High);
            canvas.DrawBitmap(avatarBitmap, currentX, currentY);
            currentX += avatarWidth * ratio + incrementalPadding;

            var scaledBitmap = new SKBitmap(cardImageInfo);
            cards.ScalePixels(scaledBitmap, SKFilterQuality.High);
            canvas.DrawBitmap(scaledBitmap, currentX, currentY);

            currentX = incrementalPadding;
            currentY += singleCardHeight;
        }

        var stream = new MemoryStream();
        surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, Quality).SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    /// <summary>
    /// Render Chinese Poker natural card combinations. Used when a player wins a game through natural cards.
    /// </summary>
    /// <param name="winner">The player who wins a game through natural cards.</param>
    /// <param name="winnerPlayedCards">All cards played by the winner, grouped by hands.</param>
    /// <param name="hands">Available hands of a Chinese Poker game.</param>
    /// <param name="deck">The deck service that contains bitmaps of cards.</param>
    /// <returns>The rendered image.</returns>
    public static async Task<Stream> RenderChinesePokerNatural(
        ChinesePokerPlayerState winner,
        Dictionary<ChinesePokerGameProgress, ChinesePokerCombination> winnerPlayedCards,
        ImmutableArray<ChinesePokerGameProgress> hands,
        DeckService deck)
    {
        var winnerAvatar = await winner.AvatarUrl.GetStreamAsync();
        var avatarBitmap = SKBitmap.Decode(winnerAvatar);
        var handBitmaps = hands
            .SelectMany(hand => winnerPlayedCards[hand].Cards
                .ToImmutableArray()
                .Sort((a, b) => a.GetChinesePokerValue().CompareTo(b.GetChinesePokerValue()))
                .Select(deck.GetBitmap))
            .ToImmutableList();

        var totalCardWidth = handBitmaps
            .Select(b => b.Width)
            .Intersperse(Padding)
            .Sum();
        
        var cardHeight = handBitmaps[0].Height + 2 * Padding;
        var avatarRatio = (float)avatarBitmap.Height / cardHeight;
        var avatarWidth = avatarBitmap.Width / avatarRatio;
        var avatarHeight = avatarBitmap.Height / avatarRatio;
        var width = totalCardWidth + avatarWidth;

        var scaleRatio = width / MaxWidth;
        var ratio = 1.0f / scaleRatio;
        var actualWidth = (int)MathF.Floor(width * ratio);
        var actualHeight = (int)MathF.Floor(cardHeight * ratio);
        var surface = SKSurface.Create(new SKImageInfo(actualWidth, actualHeight));
        var canvas = surface.Canvas;

        var incrementalPadding = Padding * ratio;
        var singleCardWidth = (int)MathF.Floor(handBitmaps[0].Width * ratio);
        var singleCardHeight = (int)MathF.Floor(cardHeight * ratio);
        var cardImageInfo = new SKImageInfo(singleCardWidth, singleCardHeight);
        var currentX = incrementalPadding;
        var avatarImageInfo = new SKImageInfo((int)MathF.Floor(avatarWidth * ratio), (int)MathF.Floor(avatarHeight * ratio));
        var avatarScaledBitmap = new SKBitmap(avatarImageInfo);
        avatarBitmap.ScalePixels(avatarScaledBitmap, SKFilterQuality.High);
        canvas.DrawBitmap(avatarScaledBitmap, currentX, 0.0f);
        currentX += avatarWidth * ratio + incrementalPadding;

        foreach (var bitmap in handBitmaps)
        {
            var scaledBitmap = new SKBitmap(cardImageInfo);
            bitmap.ScalePixels(scaledBitmap, SKFilterQuality.High);
            canvas.DrawBitmap(scaledBitmap, currentX, 0.0f);
            currentX += handBitmaps[0].Width * ratio + incrementalPadding;
        }

        var stream = new MemoryStream();
        surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, Quality).SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    /// <summary>
    /// Render the table of a competitive card game such as Contract Bridge or Chase the Pig. The players' avatars will be on the top. Beneath players' avatars are cards played by each player.
    /// </summary>
    /// <param name="deck">The deck service that contains bitmaps of cards.</param>
    /// <param name="cardsOnTable">Current cards on the table.</param>
    /// <param name="playerStates">Players who joined in the game.</param>
    /// <returns>The rendered image.</returns>
    public static async Task<Stream> RenderCompetitiveTable(
        DeckService deck,
        IEnumerable<(ulong, Card)> cardsOnTable,
        IEnumerable<IPlayerState> playerStates)
    {
        var tableCards = cardsOnTable.ToImmutableArray();
        
        var playerAvatarsTasks = tableCards
            .Select(async pair =>
            {
                var (playerId, _) = pair;
                var playerState = playerStates.First(p => p.PlayerId == playerId);
                var avatar = await playerState.AvatarUrl.GetStreamAsync();
                return KeyValuePair.Create(playerId, SKBitmap.Decode(avatar));
            });
        var playerAvatars = new Dictionary<ulong, SKBitmap>(await Task.WhenAll(playerAvatarsTasks));
        var playerCards = new Dictionary<ulong, SKBitmap>(tableCards
            .Select(pair => KeyValuePair.Create(pair.Item1, deck.GetBitmap(pair.Item2))));

        var avatarRatio = (float)playerAvatars.First().Value.Width / playerCards.First().Value.Width;
        var avatarWidth = playerAvatars.First().Value.Width / avatarRatio;
        var avatarHeight = playerAvatars.First().Value.Height / avatarRatio;
        var width = (float)playerCards
            .Values
            .Select(b => b.Width)
            .Intersperse(Padding)
            .Sum();
        var height = new[] { avatarHeight, playerCards.First().Value.Height }
            .Intersperse(Padding)
            .Sum();
        
        var scaleRatio = width / MaxWidth;
        var ratio = 1.0f / scaleRatio;
        var actualWidth = (int)MathF.Floor(width * ratio);
        var actualHeight = (int)MathF.Floor(height * ratio);
        var surface = SKSurface.Create(new SKImageInfo(actualWidth, actualHeight));
        var canvas = surface.Canvas;

        var incrementalPadding = Padding * ratio;
        var currentX = incrementalPadding;
        var currentY = incrementalPadding;
        var singleCardWidth = (int)MathF.Floor(playerCards.First().Value.Width * ratio);
        var singleCardHeight = (int)MathF.Floor(playerCards.First().Value.Height * ratio);
        var cardImageInfo = new SKImageInfo(singleCardWidth, singleCardHeight);
        var avatarImageInfo = new SKImageInfo((int)MathF.Floor(avatarWidth * ratio), (int)MathF.Floor(avatarHeight * ratio));

        foreach (var (playerId, cardBitmap) in playerCards)
        {
            var playerAvatar = playerAvatars[playerId];
            var avatarScaledBitmap = new SKBitmap(avatarImageInfo);
            playerAvatar.ScalePixels(avatarScaledBitmap, SKFilterQuality.High);
            canvas.DrawBitmap(avatarScaledBitmap, currentX, currentY);
            currentY += MathF.Floor(avatarHeight * ratio) + incrementalPadding;
            var cardScaledBitmap = new SKBitmap(cardImageInfo);
            cardBitmap.ScalePixels(cardScaledBitmap, SKFilterQuality.High);
            canvas.DrawBitmap(cardScaledBitmap, currentX, currentY);

            currentX += singleCardWidth + incrementalPadding;
            currentY = incrementalPadding;
        }
        
        var stream = new MemoryStream();
        surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, Quality).SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }
}