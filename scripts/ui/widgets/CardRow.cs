using System;
using System.Collections.Generic;
using System.Linq;

namespace Padfin;

/// <summary>A FishUI card row whose cards are Padfin's, so callers get the item each card carries.</summary>
public partial class CardRow : FishUI.CardRow
{
    public new List<Card> Cards => base.Cards.Cast<Card>().ToList();

    public CardRow() { }

    public CardRow(string title, IEnumerable<BaseItem> items, CardShape shape, int cardWidth, Action<Card> pressed, bool playOnAccept = false, bool withinSeries = false)
        : this(title, items.Select(item => new Card(item, shape, cardWidth, withinSeries: withinSeries) { PlayOnAccept = playOnAccept }), pressed)
    {
    }

    public CardRow(string title, IEnumerable<Card> cards, Action<Card> pressed)
        : base(title, cards, card => pressed((Card)card))
    {
    }
}
