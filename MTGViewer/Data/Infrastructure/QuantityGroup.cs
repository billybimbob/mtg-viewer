using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Infrastructure;

/// <summary>
/// Group of quantities (hold, want, and give back) with the same deck and
/// exact same card
/// </summary>
public class QuantityGroup : IEnumerable<Quantity>
{
    public QuantityGroup(Hold? hold, Want? want, Giveback? giveback)
    {
        _hold = hold;
        _want = want;
        _giveback = giveback;

        CheckGroup();
    }

    public QuantityGroup(Hold hold) : this(hold, null, null)
    { }

    public QuantityGroup(Want want) : this(null, want, null)
    { }

    public QuantityGroup(Giveback give) : this(null, null, give)
    { }

    public QuantityGroup(Quantity quantity)
    {
        switch (quantity)
        {
            case Hold hold:
                _hold = hold;
                break;

            case Want want:
                _want = want;
                break;

            case Giveback giveback:
                _giveback = giveback;
                break;

            default:
                throw new ArgumentException(
                    $"Unexpected quantity type {quantity.GetType().Name}", nameof(quantity));
        }

        CheckGroup();
    }

    public static IEnumerable<QuantityGroup> FromDeck(Deck deck)
    {
        var holdsById = deck.Holds.ToDictionary(h => h.CardId);
        var takesById = deck.Wants.ToDictionary(w => w.CardId);
        var givesById = deck.Givebacks.ToDictionary(g => g.CardId);

        var cardIds = holdsById.Keys
            .Union(takesById.Keys)
            .Union(givesById.Keys);

        return cardIds.Select(cid =>
            new QuantityGroup(
                holdsById.GetValueOrDefault(cid),
                takesById.GetValueOrDefault(cid),
                givesById.GetValueOrDefault(cid)));
    }

    // Guaranteed to not all be null
    private Hold? _hold;
    private Want? _want;
    private Giveback? _giveback;

    public Hold? Hold
    {
        get => _hold;
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(Hold));
            }

            _hold = value;

            CheckGroup();
        }
    }

    public Want? Want
    {
        get => _want;
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(Want));
            }

            _want = value;

            CheckGroup();
        }
    }

    public Giveback? Giveback
    {
        get => _giveback;
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(Giveback));
            }

            _giveback = value;

            CheckGroup();
        }
    }

    public TQuantity? GetQuantity<TQuantity>()
        where TQuantity : Quantity
    {
        return Hold as TQuantity
            ?? Want as TQuantity
            ?? Giveback as TQuantity;
    }

    public void AddQuantity(Quantity quantity)
    {
        switch (quantity)
        {
            case Hold hold:
                Hold = hold;
                break;

            case Want want:
                Want = want;
                break;

            case Giveback giveback:
                Giveback = giveback;
                break;

            default:
                throw new ArgumentException(
                    $"Unexpected quantity type {quantity.GetType().Name}", nameof(quantity));
        }
    }

    private void CheckGroup()
    {
        int nullCount = (Hold is null ? 0 : 1)
            + (Want is null ? 0 : 1)
            + (Giveback is null ? 0 : 1);

        if (nullCount == 0)
        {
            throw new NullReferenceException("All passed arguments are null");
        }

        if (nullCount == 1)
        {
            return;
        }

        if (!HasSameIds() && !HasSameReferences())
        {
            throw new InvalidOperationException(
                "Pairs do not reference the same location or card");
        }
    }

    private bool HasSameIds()
    {
        string cardId = CardId;
        int locationId = LocationId;

        bool sameHoldIds = Hold == null
            || (Hold.CardId == cardId && Hold.LocationId == locationId);

        bool sameTakeIds = Want == null
            || (Want.CardId == cardId && Want.LocationId == locationId);

        bool sameRetIds = Giveback == null
            || (Giveback.CardId == cardId && Giveback.LocationId == locationId);

        return sameHoldIds && sameTakeIds && sameRetIds;
    }

    private bool HasSameReferences()
    {
        var card = Card;
        var location = Location;

        bool sameActRefs = Hold == null
            || (Hold.Card == card && Hold.Location == location);

        bool sameTakeRefs = Want == null
            || (Want.Card == card && Want.Location == location);

        bool sameRetRefs = Giveback == null
            || (Giveback.Card == card && Giveback.Location == location);

        return sameActRefs && sameTakeRefs && sameRetRefs;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<Quantity> GetEnumerator()
    {
        if (Hold is not null)
        {
            yield return Hold;
        }

        if (Want is not null)
        {
            yield return Want;
        }

        if (Giveback is not null)
        {
            yield return Giveback;
        }
    }

    public string CardId =>
        Hold?.CardId
            ?? Want?.CardId
            ?? Giveback?.CardId
            ?? Card.Id;

    public Card Card =>
        Hold?.Card
            ?? Want?.Card
            ?? Giveback?.Card
            ?? default!;

    public int LocationId =>
        Hold?.LocationId
            ?? Want?.LocationId
            ?? Giveback?.LocationId
            ?? Location.Id;

    public Location Location =>
        Hold?.Location
            ?? Want?.Location
            ?? Giveback?.Location
            ?? default!;

    public int Copies =>
        (Hold?.Copies ?? 0)
            + (Want?.Copies ?? 0)
            - (Giveback?.Copies ?? 0);

    public int Total =>
        (Hold?.Copies ?? 0)
            + (Want?.Copies ?? 0)
            + (Giveback?.Copies ?? 0);
}
