using System.Collections.Immutable;
using BurstBotShared.Shared.Models.Game.Serializables;
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
            .ToImmutableList();
        var totalWidth = bitmaps.Sum(bitmap => bitmap.Width);
        var height = bitmaps.Max(bitmap => bitmap.Height);
        var surface = SKSurface.Create(new SKImageInfo(totalWidth, height));
        var canvas = surface.Canvas;

        var currentX = 0.0f;
        foreach (var bitmap in bitmaps)
        {
            canvas.DrawBitmap(bitmap, currentX, 0.0f);
            currentX += bitmap.Width;
        }

        var scaleRatio = (float) totalWidth / MaxWidth;
        var ratio = MathF.Floor(1.0f / scaleRatio);
        canvas.Scale(ratio, ratio);

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
}