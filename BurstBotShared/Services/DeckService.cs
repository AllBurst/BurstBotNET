using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using BurstBotShared.Shared.Models.Game.Serializables;
using SkiaSharp;

namespace BurstBotShared.Services;

public class DeckService
{
    private const string SpritePath = "Assets/cards";
    private const string UserInterfaceLogoPath = "/UI_logo";
    private readonly Dictionary<Card, Lazy<SKBitmap>> _cardSprites = new(54);
    private readonly Lazy<SKBitmap> _cardBack;
    private readonly Lazy<SKBitmap> _cardJoker1;
    private readonly Lazy<SKBitmap> _cardJoker2;

    public DeckService()
    {
        if (!Directory.Exists(SpritePath + UserInterfaceLogoPath))
        {
            Directory.CreateDirectory(SpritePath + UserInterfaceLogoPath);
        }

        var suits = Enum.GetValues<Suit>();
        var numbers = Enumerable
            .Range(1, 13)
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

        _cardBack = new Lazy<SKBitmap>(() => SKBitmap.Decode($"{SpritePath}/back.png"));
        _cardJoker1 = new Lazy<SKBitmap>(() => SKBitmap.Decode($"{SpritePath}/joker.png"));
        _cardJoker2 = new Lazy<SKBitmap>(() => SKBitmap.Decode($"{SpritePath}/joker03.png"));
    }

    [Pure]
    public SKBitmap GetBitmap(Card card)
    {
        if (!card.IsFront) return _cardBack.Value;
        return card.Number == 0 ? GetJoker(card) : _cardSprites[card].Value;
    }

    [Pure]
    private SKBitmap GetJoker(Card card)
        => card.Suit switch
        {
            Suit.Club or Suit.Diamond => _cardJoker1.Value,
            Suit.Heart or Suit.Spade => _cardJoker2.Value,
            _ => _cardJoker1.Value
        };

    private static SKBitmap LoadBitmap(Suit suit, string rank)
        => SKBitmap.Decode($"{SpritePath}/{suit.ToString().ToLowerInvariant()}{rank}.png");
}