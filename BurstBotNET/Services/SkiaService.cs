using System.Collections.Immutable;
using BurstBotNET.Shared.Models.Game.Serializables;
using SkiaSharp;

namespace BurstBotNET.Services;

public static class SkiaService
{
    private const int DefaultMaxWidth = 2048;
    private const int DefaultQuality = 95;
    
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

        var scaleRatio = (float) totalWidth / DefaultMaxWidth;
        var ratio = MathF.Floor(1.0f / scaleRatio);
        canvas.Scale(ratio, ratio);

        var stream = new MemoryStream();
        surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, DefaultQuality).SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }
}