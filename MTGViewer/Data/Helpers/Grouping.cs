using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

#nullable enable
namespace MTGViewer.Data;

/// <summary>Group of amounts with the same deck and same card name</summary>
public class CardNameGroup : IEnumerable<Amount>
{
    public CardNameGroup(IEnumerable<Amount> amounts)
    {
        _amounts = new(amounts);

        if (!_amounts.Any())
        {
            throw new ArgumentException("The amounts are empty");
        }

        if (_amounts.Any(ca => ca.Card.Name != Name))
        {
            throw new ArgumentException("All cards do not match the name");
        }

        if (_amounts.Any(ca => ca.Card.ManaCost != ManaCost))
        {
            throw new ArgumentException("All cards do not match the mana cost");
        }

        if (_amounts.Any(ca => ca.LocationId != LocationId && ca.Location != Location))
        {
            throw new ArgumentException("All cards do not have the same location");
        }
    }

    public CardNameGroup(params Amount[] amounts)
        : this(amounts.AsEnumerable())
    { }


    // guranteed >= 1 CardAmounts in linkedlist
    private readonly LinkedList<Amount> _amounts;


    private Amount First => _amounts.First!.Value;

    public string Name => First.Card.Name;
    public string ManaCost => First.Card.ManaCost;

    public IEnumerable<string> CardIds => _amounts.Select(ca => ca.CardId);
    public IEnumerable<Card> Cards => _amounts.Select(ca => ca.Card);


    public int LocationId => First.LocationId;
    public Location Location => First.Location;


    public int NumCopies
    {
        get => _amounts.Sum(ca => ca.NumCopies);
        set
        {
            var lastCycle = _amounts.Last!.Value;
            int change = NumCopies - value;

            while (change < 0 || change > 0 && lastCycle.NumCopies > 0)
            {
                int mod = Math.Min(change, First.NumCopies);

                First.NumCopies -= mod;
                change -= mod;

                if (First.NumCopies == 0)
                {
                    // cycle amount
                    var firstLink = _amounts.First!;
                    _amounts.Remove(firstLink);
                    _amounts.AddLast(firstLink);
                }
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<Amount> GetEnumerator() => _amounts.GetEnumerator();
}



/// <summary>Group of wants with the same deck and same card name</summary>
public class WantNameGroup : IEnumerable<Want>
{
    public WantNameGroup(IEnumerable<Want> wants)
    {
        _wants = new(wants);

        if (!_wants.Any())
        {
            throw new ArgumentException("The exchanges are empty");
        }

        if (_wants.Any(w => w.Card.Name != Name))
        {
            throw new ArgumentException("All exchanges do not match the name");
        }

        if (_wants.Any(w => w.Card.ManaCost != ManaCost))
        {
            throw new ArgumentException("All exchanges do not match the mana cost");
        }

        if (_wants.Any(w => w.LocationId != LocationId && w.Location != Location))
        {
            throw new ArgumentException("All exchanges do not have the same location");
        }
    }

    public WantNameGroup(params Want[] wants)
        : this(wants.AsEnumerable())
    { }


    // guranteed >= 1 CardAmounts in linkedlist
    private readonly LinkedList<Want> _wants;


    private Want First => _wants.First!.Value;

    public string Name => First.Card.Name;
    public string ManaCost => First.Card.ManaCost;

    public IEnumerable<string> CardIds => _wants.Select(ca => ca.CardId);
    public IEnumerable<Card> Cards => _wants.Select(ca => ca.Card);


    public int LocationId => First.LocationId;
    public Location Location => First.Location;


    public int NumCopies
    {
        get => _wants.Sum(ca => ca.NumCopies);
        set
        {
            int change = NumCopies - value;
            while (change < 0 || change > 0 && First.NumCopies > 0)
            {
                int mod = Math.Min(change, First.NumCopies);

                First.NumCopies -= mod;
                change -= mod;

                if (First.NumCopies == 0)
                {
                    // cycle amount
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
/// Group of quantities (amount, want, and give back) with the same deck and 
/// exact same card
/// </summary>
public class QuantityGroup : IEnumerable<Quantity>
{
    public QuantityGroup(Amount? amount, Want? want, GiveBack? giveBack)
    {
        _amount = amount;
        _want = want;
        _giveBack = giveBack;

        CheckGroup();
    }

    public QuantityGroup(Amount amount)
        : this(amount, null, null)
    { }

    public QuantityGroup(Want want)
        : this(null, want, null)
    { }

    public QuantityGroup(GiveBack giveBack)
        : this(null, null, giveBack)
    { }

    public QuantityGroup(Quantity quantity)
    {
        switch(quantity)
        {
            case Amount amount:
                _amount = amount;
                break;

            case Want want:
                _want = want;
                break;

            case GiveBack giveBack:
                _giveBack = giveBack;
                break;
        }

        CheckGroup();
    }


    // Guaranteed to not all be null
    private Amount? _amount;
    private Want? _want;
    private GiveBack? _giveBack;


    public Amount? Amount
    {
        get => _amount;
        set
        {
            if (value is null)
            {
                throw new ArgumentException("Amount is not a valid actual amount");
            }

            _amount = value;

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
                throw new ArgumentException("Amount is not a valid take amount");
            }

            _want = value;

            CheckGroup();
        }
    }

    public GiveBack? GiveBack
    {
        get => _giveBack;
        set
        {
            if (value is null)
            {
                throw new ArgumentException("Amount is not a valid return amount");
            }

            _giveBack = value;

            CheckGroup();
        }
    }


    private void CheckGroup()
    {
        var nullCount = (Amount is null ? 0 : 1)
            + (Want is null ? 0 : 1)
            + (GiveBack is null ? 0 : 1);

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

        var sameActIds = Amount == null
            || Amount.CardId == cardId && Amount.LocationId == locationId;

        var sameTakeIds = Want == null 
            || Want.CardId == cardId && Want.LocationId == locationId;

        var sameRetIds = GiveBack == null 
            || GiveBack.CardId == cardId && GiveBack.LocationId == locationId;

        return sameActIds && sameTakeIds && sameRetIds;
    }

    private bool HasSameReferences()
    {
        var card = Card;
        var location = Location;

        var sameActRefs = Amount == null 
            || Amount.Card == card && Amount.Location == location;

        var sameTakeRefs = Want == null 
            || Want.Card == card && Want.Location == location;

        var sameRetRefs = GiveBack == null 
            || GiveBack.Card == card && GiveBack.Location == location;

        return sameActRefs && sameTakeRefs && sameRetRefs;
    }


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<Quantity> GetEnumerator()
    {
        if (Amount is not null)
        {
            yield return Amount;
        }

        if (Want is not null)
        {
            yield return Want;
        }

        if (GiveBack is not null)
        {
            yield return GiveBack;
        }
    }


    public string CardId =>
        Amount?.CardId
            ?? Want?.CardId
            ?? GiveBack?.CardId
            ?? Card.Id;

    public Card Card =>
        Amount?.Card
            ?? Want?.Card
            ?? GiveBack?.Card
            ?? null!;


    public int LocationId =>
        Amount?.LocationId
            ?? Want?.LocationId
            ?? GiveBack?.LocationId
            ?? Location.Id;

    public Location Location =>
        Amount?.Location
            ?? Want?.Location
            ?? GiveBack?.Location
            ?? null!;


    public int NumCopies =>
        (Amount?.NumCopies ?? 0)
            + (Want?.NumCopies ?? 0)
            - (GiveBack?.NumCopies ?? 0);

    public int Total =>
        (Amount?.NumCopies ?? 0)
            + (Want?.NumCopies ?? 0)
            + (GiveBack?.NumCopies ?? 0);
}



/// <summary>
/// Group of quantities with the same deck and same card name
/// </summary>
public class QuantityNameGroup : IEnumerable<QuantityGroup>
{
    public QuantityNameGroup(
        IEnumerable<Amount> amounts, 
        IEnumerable<Want> wants,
        IEnumerable<GiveBack>? giveBacks = null)
    {
        // do a full outer join
        var amountTable = amounts.ToDictionary(ca => ca.CardId ?? ca.Card.Id);
        var wantTable = wants.ToDictionary(ca => ca.CardId ?? ca.Card.Id);
        var giveTable = giveBacks?.ToDictionary(ca => ca.CardId ?? ca.Card.Id);

        var allCardIds = amountTable.Keys
            .Union(wantTable.Keys)
            .Union(giveTable?.Keys ?? Enumerable.Empty<string>());

        _quantityGroups = allCardIds
            .Select(cid =>
                new QuantityGroup(
                    amountTable.GetValueOrDefault(cid),
                    wantTable.GetValueOrDefault(cid),
                    giveTable?.GetValueOrDefault(cid) ))
            .ToList();

        CheckGroups();
    }


    private readonly IReadOnlyList<QuantityGroup> _quantityGroups;

    private void CheckGroups()
    {
        var name = Name;
        var locationId = LocationId;
        var location = Location;

        var valuesSame = this.All(rg =>
            rg.Card.Name == name 
                && rg.LocationId == locationId
                && rg.Location == location);

        if (!valuesSame)
        {
            throw new ArgumentException(
                "Pairs do not reference the same location or card");
        }
    }


    public string Name =>
        this.First().Card.Name;

    public Location Location =>
        this.First().Location;

    public int LocationId =>
        this.First().LocationId;

    public int NumCopies =>
        this.Sum(rg => rg.NumCopies);

    public int InDeck =>
        this.Sum(rg => rg.Amount?.NumCopies ?? 0);

    public int Requests =>
        this.Sum(rg => rg.Want?.NumCopies ?? 0) - this.Sum(rg => rg.GiveBack?.NumCopies ?? 0);


    public IEnumerable<string> CardIds =>
        this.Select(da => da.CardId);

    public IEnumerable<Card> Cards =>
        this.Select(da => da.Card);


    IEnumerator IEnumerable.GetEnumerator() => 
        GetEnumerator();

    public IEnumerator<QuantityGroup> GetEnumerator() =>
        _quantityGroups.GetEnumerator();
}



public record Transfer(
    Transaction Transaction, 
    Location? From, 
    Location To,
    IReadOnlyList<Change> Changes);



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
            throw new ArgumentException("The trade group is empty");
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
            throw new ArgumentException("All trade destinations are not the same");
        }
    }


    public Deck Target => _useToTarget ? _first.To : _first.From;
    public int TargetId => Target?.Id
        ?? (_useToTarget ? _first.ToId : _first.FromId);


    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<Trade> GetEnumerator() => _trades.GetEnumerator();

}