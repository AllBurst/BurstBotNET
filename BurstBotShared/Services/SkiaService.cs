using System.Collections.Immutable;
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
    
    public static Stream RenderDeck(DeckService deck, IEnumerable<Card> cards)
    {
        var bitmaps = cards
            .Select(deck.GetBitmap)
            .ToImmutableArray();
        var totalWidth = bitmaps.Sum(bitmap => bitmap.Width);
        var height = bitmaps.Max(bitmap => bitmap.Height);
        var scaleRatio = (float) totalWidth / MaxWidth;
        var ratio = 1.0f / scaleRatio;
        var actualWidth = (int)MathF.Floor(totalWidth * ratio);
        var actualHeight = (int)MathF.Floor(height * ratio);
        var surface = SKSurface.Create(new SKImageInfo(actualWidth, actualHeight));
        var canvas = surface.Canvas;

        var currentX = 0.0f;
        var singleCardWidth = (int)MathF.Floor(bitmaps[0].Width * ratio);
        var singleCardHeight = (int)MathF.Floor(bitmaps[0].Height * ratio);
        var imageInfo = new SKImageInfo(singleCardWidth, singleCardHeight);
        foreach (var bitmap in bitmaps)
        {
            var scaledBitmap = new SKBitmap(imageInfo);
            bitmap.ScalePixels(scaledBitmap, SKFilterQuality.High);
            canvas.DrawBitmap(scaledBitmap, currentX, 0.0f);
            currentX += singleCardWidth;
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
        var width = bitmaps[0].Width * 5;
        var height = bitmaps[0].Height * 3;
        var scaleRatio = (float) width / MaxWidth;
        var ratio = 1.0f / scaleRatio;
        var actualWidth = (int)MathF.Floor(width * ratio);
        var actualHeight = (int)MathF.Floor(height * ratio);
        var surface = SKSurface.Create(new SKImageInfo(actualWidth, actualHeight));
        var canvas = surface.Canvas;

        var currentX = 0.0f;
        var currentY = 0.0f;
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
            currentX += singleCardWidth;
            if (index is not (3 or 8)) continue;
            currentX = 0.0f;
            currentY += singleCardHeight;
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
        var width = totalCardWidth + avatarWidth;
        var height = cardHeight * 4.0f;

        var scaleRatio = width / MaxWidth;
        var ratio = 1.0f / scaleRatio;
        var actualWidth = (int)MathF.Floor(width * ratio);
        var actualHeight = (int)MathF.Floor(height * ratio);
        var surface = SKSurface.Create(new SKImageInfo(actualWidth, actualHeight));
        var canvas = surface.Canvas;

        var currentX = 0.0f;
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
            currentX += avatarWidth * ratio;

            var scaledBitmap = new SKBitmap(cardImageInfo);
            cards.ScalePixels(scaledBitmap, SKFilterQuality.High);
            canvas.DrawBitmap(scaledBitmap, currentX, currentY);

            currentX = 0.0f;
            currentY += singleCardHeight;
        }

        var stream = new MemoryStream();
        surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, Quality).SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }
}