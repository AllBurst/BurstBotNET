using BurstBotShared.Shared.Models.Game.Serializables;

namespace BurstBotShared.Shared.Models.Game;

public class CardEqualityComparer : IEqualityComparer<Card>
{
    public bool Equals(Card? x, Card? y)
    {
        return y != null && x != null && x.Suit == y.Suit && x.Number == y.Number && x.IsFront == y.IsFront;
    }

    public int GetHashCode(Card obj)
    {
        return HashCode.Combine(obj.Suit.ToString(), obj.Number, obj.IsFront);
    }
}