using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MTGViewer.Data
{
    /// <summary>Group of card amounts with the same card name</summary>
    public class CardNameGroup : IEnumerable<CardAmount>
    {
        public CardNameGroup(IEnumerable<CardAmount> amounts)
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

        public CardNameGroup(params CardAmount[] amounts)
            : this(amounts.AsEnumerable())
        { }


        // guranteed >= 1 CardAmounts in linkedlist
        private readonly LinkedList<CardAmount> _amounts;


        private CardAmount First => _amounts.First!.Value;

        public string Name => First.Card.Name;
        public string ManaCost => First.Card.ManaCost;

        public IEnumerable<string> CardIds => _amounts.Select(ca => ca.CardId);
        public IEnumerable<Card> Cards => _amounts.Select(ca => ca.Card);


        public int LocationId => First.LocationId;
        public Location Location => First.Location;


        public int Amount
        {
            get => _amounts.Sum(ca => ca.Amount);
            set
            {
                var lastCycle = _amounts.Last!.Value;
                int change = Amount - value;

                while (change < 0 || change > 0 && lastCycle.Amount > 0)
                {
                    int mod = Math.Min(change, First.Amount);

                    First.Amount -= mod;
                    change -= mod;

                    if (First.Amount == 0)
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

        public IEnumerator<CardAmount> GetEnumerator() => _amounts.GetEnumerator();
    }



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

            if (_wants.Any(w => w.DeckId != DeckId && w.Deck != Deck))
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


        public int DeckId => First.DeckId;
        public Deck Deck => First.Deck;


        public int Amount
        {
            get => _wants.Sum(ca => ca.Amount);
            set
            {
                int change = Amount - value;
                while (change < 0 || change > 0 && First.Amount > 0)
                {
                    int mod = Math.Min(change, First.Amount);

                    First.Amount -= mod;
                    change -= mod;

                    if (First.Amount == 0)
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



    /// <summary>Group of deck amounts with the same deck and same card</summary>
    public class QuantityGroup
    {
        public QuantityGroup(CardAmount? amount, Want? want, GiveBack? giveBack)
        {
            _actual = amount;
            _want = want;
            _giveBack = giveBack;

            CheckGroup();
        }

        public QuantityGroup(CardAmount amount)
            : this(amount, null, null)
        { }

        public QuantityGroup(Want want)
            : this(null, want, null)
        { }

        public QuantityGroup(GiveBack giveBack)
            : this(null, null, giveBack)
        { }



        // Guaranteed to not all be null
        private CardAmount? _actual;
        private Want? _want;
        private GiveBack? _giveBack;


        public CardAmount? Actual
        {
            get => _actual;
            set
            {
                if (value is null)
                {
                    throw new ArgumentException("Amount is not a valid actual amount");
                }

                _actual = value;

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
            var nullCount = (Actual is null ? 0 : 1)
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

            var sameActIds = Actual == null
                || Actual.CardId == cardId && Actual.LocationId == locationId;

            var sameTakeIds = Want == null 
                || Want.CardId == cardId && Want.DeckId == locationId;

            var sameRetIds = GiveBack == null 
                || GiveBack.CardId == cardId && GiveBack.DeckId == locationId;

            return sameActIds && sameTakeIds && sameRetIds;
        }

        private bool HasSameReferences()
        {
            var card = Card;
            var location = Location;

            var sameActRefs = Actual == null 
                || Actual.Card == card && Actual.Location == location;

            var sameTakeRefs = Want == null 
                || Want.Card == card && Want.Deck == location;

            var sameRetRefs = GiveBack == null 
                || GiveBack.Card == card && GiveBack.Deck == location;

            return sameActRefs && sameTakeRefs && sameRetRefs;
        }


        public string CardId =>
            Actual?.CardId
                ?? Want?.CardId
                ?? GiveBack?.CardId
                ?? Card.Id;

        public Card Card =>
            Actual?.Card
                ?? Want?.Card
                ?? GiveBack?.Card
                ?? null!;


        public int LocationId =>
            Actual?.LocationId
                ?? Want?.DeckId
                ?? GiveBack?.DeckId
                ?? Location.Id;

        public Location Location =>
            Actual?.Location
                ?? Want?.Deck
                ?? GiveBack?.Deck
                ?? null!;


        public int Amount =>
            (Actual?.Amount ?? 0)
                + (Want?.Amount ?? 0)
                - (GiveBack?.Amount ?? 0);
    }



    /// <summary>
    /// Group of card amounts with the same card name, and the same deck
    /// </summary>
    public class QuantityNameGroup : IEnumerable<QuantityGroup>
    {
        public QuantityNameGroup(
            IEnumerable<CardAmount> amounts, 
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

        public int Amount =>
            this.Sum(rg => rg.Amount);

        public int InDeck =>
            this.Sum(rg => rg.Actual?.Amount ?? 0);

        public int Requests =>
            this.Sum(rg => rg.Want?.Amount ?? 0) - this.Sum(rg => rg.GiveBack?.Amount ?? 0);


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
        IReadOnlyList<Change> Changes) { }



    /// <summary>Group of trades with the same proposer and to deck</summary>
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
}