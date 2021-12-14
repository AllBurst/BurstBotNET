using System.Collections.Immutable;
using BurstBotNET.Shared.Models.Game.Serializables;
using SkiaSharp;

namespace BurstBotNET.Services;

public class DeckService
{
    private const string DefaultSpritePath = "Assets/cards";
    private const string UserInterfaceLogoPath = "/UI_logo";
    private readonly Dictionary<Card, Lazy<SKBitmap>> _cardSprites = new();
    private readonly Lazy<SKBitmap> _cardBack;

    public DeckService()
    {
        if (!Directory.Exists(DefaultSpritePath + UserInterfaceLogoPath))
        {
            Directory.CreateDirectory(DefaultSpritePath + UserInterfaceLogoPath);
        }

        var suits = new[] { Suit.Spade, Suit.Heart, Suit.Diamond, Suit.Club };
        var numbers = Enumerable
            .Range(0, 14)
            .ToImmutableDictionary(n => n, n => n switch
            {
                1 => "A",
                11 => "J",
                12 => "Q",
                13 => "K",
                _ => n.ToString()
            });
        
        foreach (var suit in suits)
        {
            foreach (var (n, s) in numbers)
            {
                _cardSprites.Add(new Card
                {
                    IsFront = true,
                    Number = n,
                    Suit = suit
                }, new Lazy<SKBitmap>(() => LoadBitmap(suit, s switch
                {
                    "A" or "J" or "Q" or "K" => s,
                    _ => s.PadLeft(2, '0')
                })));
            }
        }

        _cardBack = new Lazy<SKBitmap>(() => SKBitmap.Decode($"{DefaultSpritePath}/back.png"));
    }

    public SKBitmap GetBitmap(Card card)
        => card.IsFront ? _cardSprites[card].Value : _cardBack.Value;

    private static SKBitmap LoadBitmap(Suit suit, string character)
        => SKBitmap.Decode($"{DefaultSpritePath}/{suit.ToString().ToLowerInvariant()}{character}.png");
}