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



    public class RequestNameGroup : IEnumerable<CardRequest>
    {
        public RequestNameGroup(IEnumerable<CardRequest> requests)
        {
            _requests = new(requests);

            if (!_requests.Any())
            {
                throw new ArgumentException("The exchanges are empty");
            }

            if (_requests.Any(ex => ex.Card.Name != Name))
            {
                throw new ArgumentException("All exchanges do not match the name");
            }

            if (_requests.Any(ex => ex.Card.ManaCost != ManaCost))
            {
                throw new ArgumentException("All exchanges do not match the mana cost");
            }

            if (_requests.Any(ex => ex.IsReturn != IsReturn))
            {
                throw new ArgumentException("All exchanges are not matching trades");
            }

            if (_requests.Any(ex => ex.TargetId != TargetId && ex.Target != Target))
            {
                throw new ArgumentException("All exchanges do not have the same location");
            }
        }

        public RequestNameGroup(params CardRequest[] amounts)
            : this(amounts.AsEnumerable())
        { }


        // guranteed >= 1 CardAmounts in linkedlist
        private readonly LinkedList<CardRequest> _requests;


        private CardRequest First => _requests.First!.Value;

        public string Name => First.Card.Name;
        public string ManaCost => First.Card.ManaCost;

        public IEnumerable<string> CardIds => _requests.Select(ca => ca.CardId);
        public IEnumerable<Card> Cards => _requests.Select(ca => ca.Card);


        public int TargetId => First.TargetId;
        public Deck Target => First.Target;

        public bool IsReturn => First.IsReturn;


        public int Amount
        {
            get => _requests.Sum(ca => ca.Amount);
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
                        var firstLink = _requests.First!;
                        _requests.Remove(firstLink);
                        _requests.AddLast(firstLink);
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<CardRequest> GetEnumerator() => _requests.GetEnumerator();
    }



    /// <summary>Group of deck amounts with the same deck and same card</summary>
    public class AmountRequestGroup
    {
        public AmountRequestGroup(CardAmount? amount, IEnumerable<CardRequest> requests)
        {
            _actual = amount;
            _take = requests.FirstOrDefault(cr => !cr.IsReturn);
            _return = requests.FirstOrDefault(cr => cr.IsReturn);

            CheckGroup();
        }

        public AmountRequestGroup(CardAmount? amount, params CardRequest[] exchanges)
            : this(amount, exchanges.AsEnumerable())
        { }

        public AmountRequestGroup(IEnumerable<CardRequest> exchanges)
            : this(null, exchanges)
        { }

        public AmountRequestGroup(params CardRequest[] exchanges)
            : this(exchanges.AsEnumerable())
        { }


        // Guaranteed to not all be null
        private CardAmount? _actual;
        private CardRequest? _take;
        private CardRequest? _return;


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

        public CardRequest? Take
        {
            get => _take;
            set
            {
                if (value is null || !value.IsReturn)
                {
                    throw new ArgumentException("Amount is not a valid take amount");
                }

                _take = value;

                CheckGroup();
            }
        }

        public CardRequest? Return
        {
            get => _return;
            set
            {
                if (value is null || value.IsReturn)
                {
                    throw new ArgumentException("Amount is not a valid return amount");
                }

                _return = value;

                CheckGroup();
            }
        }


        private void CheckGroup()
        {
            var nullCount = (Actual is null ? 0 : 1)
                + (Take is null ? 0 : 1)
                + (Return is null ? 0 : 1);

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

            var sameTakeIds = Take == null 
                || Take.CardId == cardId && Take.TargetId == locationId;

            var sameRetIds = Return == null 
                || Return.CardId == cardId && Return.TargetId == locationId;

            return sameActIds && sameTakeIds && sameRetIds;
        }

        private bool HasSameReferences()
        {
            var card = Card;
            var location = Location;

            var sameActRefs = Actual == null 
                || object.ReferenceEquals(Actual.Card, card)
                    && object.ReferenceEquals(Actual.Location, location);

            var sameTakeRefs = Take == null 
                || object.ReferenceEquals(Take.Card, card)
                    && object.ReferenceEquals(Take.Target, location);

            var sameRetRefs = Return == null 
                || object.ReferenceEquals(Return.Card, card)
                    && object.ReferenceEquals(Return.Target, location);

            return sameActRefs && sameTakeRefs && sameRetRefs;
        }


        public string CardId =>
            Actual?.CardId
                ?? Take?.CardId
                ?? Return?.CardId
                ?? Card.Id;

        public Card Card =>
            Actual?.Card
                ?? Take?.Card
                ?? Return?.Card
                ?? null!;


        public int LocationId =>
            Actual?.LocationId
                ?? Take?.TargetId
                ?? Return?.TargetId
                ?? Location.Id;

        public Location Location =>
            Actual?.Location
                ?? Take?.Target
                ?? Return?.Target
                ?? null!;


        public int Amount =>
            (Actual?.Amount ?? 0)
                + (Take?.Amount ?? 0)
                - (Return?.Amount ?? 0);
    }



    /// <summary>
    /// Group of card amounts with the same card name, and the same deck
    /// </summary>
    public class AmountRequestNameGroup : IEnumerable<AmountRequestGroup>
    {
        public AmountRequestNameGroup(IEnumerable<CardAmount> amounts, IEnumerable<CardRequest> requests)
        {
            // do a full outer join
            var amountTable = amounts.ToDictionary(ca => ca.CardId ?? ca.Card.Id);
            var exchangeLookup = requests.ToLookup(ex => ex.CardId ?? ex.Card.Id);

            var allCardIds = exchangeLookup
                .Select(g => g.Key)
                .Union(amountTable.Keys);

            _requestGroups = allCardIds
                .Select(cid =>
                {
                    amountTable.TryGetValue(cid, out var amount);
                    return new AmountRequestGroup(amount, exchangeLookup[cid]);
                })
                .ToList();

            CheckGroups();
        }

        private readonly IReadOnlyList<AmountRequestGroup> _requestGroups;


        private void CheckGroups()
        {
            var name = Name;
            var locationId = LocationId;
            var location = Location;

            var valuesSame = this.All(rg =>
                rg.Card.Name == name 
                    && rg.LocationId == locationId
                    && object.ReferenceEquals(rg.Location, location));

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
            this.Sum(rg => rg.Take?.Amount ?? 0) - this.Sum(rg => rg.Return?.Amount ?? 0);


        public IEnumerable<string> CardIds =>
            this.Select(da => da.CardId);

        public IEnumerable<Card> Cards =>
            this.Select(da => da.Card);


        IEnumerator IEnumerable.GetEnumerator() => 
            GetEnumerator();

        public IEnumerator<AmountRequestGroup> GetEnumerator() =>
            _requestGroups.GetEnumerator();
    }




    /// <summary>Group of trades with the same proposer and to deck</summary>
    public class TradeSet : IEnumerable<Trade>
    {
        private IEnumerable<Trade> _trades;

        public TradeSet(IEnumerable<Trade> trades)
        {
            _trades = trades.ToList();

            if (!_trades.Any())
            {
                throw new ArgumentException("The trade group is empty");
            }

            if (_trades.Any(t => t.ToId != ToId && t.To != To))
            {
                throw new ArgumentException("All trade destinations are not the same");
            }
        }


        private Trade First => _trades.First();

        public int ToId => (int)First.ToId!;
        public Deck To => First.To!;


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<Trade> GetEnumerator() => _trades.GetEnumerator();
    }
}