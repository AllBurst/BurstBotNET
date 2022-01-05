using System.Collections.Immutable;
using BurstBotShared.Shared.Extensions;
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

    public static Stream RenderCard(DeckService deck, Card card)
    {
        var stream = new MemoryStream();
        deck.GetBitmap(card).Encode(SKEncodedImageFormat.Jpeg, Quality).SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

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
}