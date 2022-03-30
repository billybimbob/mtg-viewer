using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data;

/// <summary>Group of holds with the same card name</summary>
public class HoldNameGroup : IEnumerable<Hold>
{
    public HoldNameGroup(IEnumerable<Hold> holds)
    {
        _holds = new(holds);

        if (!_holds.Any())
        {
            throw new ArgumentException($"{nameof(holds)} is empty", nameof(holds));
        }

        if (_holds.Any(h => h.Card.Name != Name))
        {
            throw new ArgumentException("All cards do not match the name", nameof(holds));
        }

        if (_holds.Any(h => h.Card.ManaCost != ManaCost))
        {
            throw new ArgumentException("All cards do not match the mana cost", nameof(holds));
        }
    }

    public HoldNameGroup(params Hold[] holds)
        : this(holds.AsEnumerable())
    { }


    // guranteed >= 1 Holds in linkedlist
    private readonly LinkedList<Hold> _holds;


    private Hold First => _holds.First!.Value;

    public string Name => First.Card.Name;
    public string? ManaCost => First.Card.ManaCost;

    public IEnumerable<string> CardIds => _holds.Select(h => h.CardId);
    public IEnumerable<Card> Cards => _holds.Select(h => h.Card);


    public int Copies
    {
        get => _holds.Sum(h => h.Copies);
        set
        {
            var lastCycle = _holds.Last!.Value;
            int change = Copies - value;

            while (change < 0 || change > 0 && lastCycle.Copies > 0)
            {
                int mod = Math.Min(change, First.Copies);

                First.Copies -= mod;
                change -= mod;

                if (First.Copies == 0)
                {
                    // cycle hold
                    var firstLink = _holds.First!;
                    _holds.Remove(firstLink);
                    _holds.AddLast(firstLink);
                }
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<Hold> GetEnumerator() => _holds.GetEnumerator();
}



/// <summary>Group of wants with the same card name</summary>
public class WantNameGroup : IEnumerable<Want>
{
    public WantNameGroup(IEnumerable<Want> wants)
    {
        _wants = new(wants);

        if (!_wants.Any())
        {
            throw new ArgumentException("The exchanges are empty", nameof(wants));
        }

        if (_wants.Any(w => w.Card.Name != Name))
        {
            throw new ArgumentException("All exchanges do not match the name", nameof(wants));
        }

        if (_wants.Any(w => w.Card.ManaCost != ManaCost))
        {
            throw new ArgumentException("All exchanges do not match the mana cost", nameof(wants));
        }
    }

    public WantNameGroup(params Want[] wants)
        : this(wants.AsEnumerable())
    { }


    // guranteed >= 1 Want in linkedlist
    private readonly LinkedList<Want> _wants;


    private Want First => _wants.First!.Value;

    public string Name => First.Card.Name;
    public string? ManaCost => First.Card.ManaCost;

    public IEnumerable<string> CardIds => _wants.Select(w => w.CardId);
    public IEnumerable<Card> Cards => _wants.Select(w => w.Card);


    public int Copies
    {
        get => _wants.Sum(w => w.Copies);
        set
        {
            int change = Copies - value;
            while (change < 0 || change > 0 && First.Copies > 0)
            {
                int mod = Math.Min(change, First.Copies);

                First.Copies -= mod;
                change -= mod;

                if (First.Copies == 0)
                {
                    var firstLink = _wants.First!;
                    _wants.Remove(firstLink);
                    _wants.AddLast(firstLink);
                }
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<Want> GetEnumerator() => _wants.GetEnumerator();
}



/// <summary>
/// Group of quantities (hold, want, and give back) with the same deck and 
/// exact same card
/// </summary>
public class QuantityGroup : IEnumerable<Quantity>
{
    public QuantityGroup(Hold? hold, Want? want, Giveback? giveBack)
    {
        _hold = hold;
        _want = want;
        _giveBack = giveBack;

        CheckGroup();
    }

    public QuantityGroup(Hold hold)
        : this(hold, null, null)
    { }

    public QuantityGroup(Want want)
        : this(null, want, null)
    { }

    public QuantityGroup(Giveback giveBack)
        : this(null, null, giveBack)
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

            case Giveback giveBack:
                _giveBack = giveBack;
                break;
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
    private Giveback? _giveBack;


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
        get => _giveBack;
        set
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(Giveback));
            }

            _giveBack = value;

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

    public void AddQuantity<TQuantity>(TQuantity quantity)
        where TQuantity : Quantity
    {
        switch (quantity)
        {
            case Hold hold:
                Hold = hold;
                break;

            case Want want:
                Want = want;
                break;

            case Giveback giveBack:
                Giveback = giveBack;
                break;
        }
    }


    private void CheckGroup()
    {
        var nullCount = (Hold is null ? 0 : 1)
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
            throw new ArgumentException(
                "Pairs do not reference the same location or card");
        }
    }

    private bool HasSameIds()
    {
        var cardId = CardId;
        var locationId = LocationId;

        var sameHoldIds = Hold == null
            || Hold.CardId == cardId && Hold.LocationId == locationId;

        var sameTakeIds = Want == null
            || Want.CardId == cardId && Want.LocationId == locationId;

        var sameRetIds = Giveback == null
            || Giveback.CardId == cardId && Giveback.LocationId == locationId;

        return sameHoldIds && sameTakeIds && sameRetIds;
    }

    private bool HasSameReferences()
    {
        var card = Card;
        var location = Location;

        var sameActRefs = Hold == null
            || Hold.Card == card && Hold.Location == location;

        var sameTakeRefs = Want == null
            || Want.Card == card && Want.Location == location;

        var sameRetRefs = Giveback == null
            || Giveback.Card == card && Giveback.Location == location;

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



/// <summary>Group of trades with either the same To or From deck</summary>
public class TradeSet : IEnumerable<Trade>
{
    private readonly IReadOnlyCollection<Trade> _trades;
    private readonly bool _useToTarget;
    private readonly Trade _first;

    public TradeSet(IEnumerable<Trade> trades, bool useToTarget)
    {
        _trades = trades.ToList();

        if (!_trades.Any())
        {
            throw new ArgumentException("The trade group is empty", nameof(trades));
        }

        _first = _trades.First();

        if (useToTarget
            && _first.To != null
            && _trades.All(t => t.To == _first.To))
        {
            _useToTarget = true;
        }
        else if (!useToTarget
            && _first.From != null
            && _trades.All(t => t.From == _first.From))
        {
            _useToTarget = false;
        }
        else
        {
            throw new ArgumentException("All trade destinations are not the same", nameof(trades));
        }
    }


    public Deck Target => _useToTarget ? _first.To : _first.From;
    public int TargetId => Target?.Id
        ?? (_useToTarget ? _first.ToId : _first.FromId);


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<Trade> GetEnumerator() => _trades.GetEnumerator();

}
