using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MTGViewer.Data
{
    // /// <summary>Group of card amounts with the exact same card</summary>
    // public class CardGroup : IEnumerable<CardAmount>
    // {
    //     public CardGroup(IEnumerable<CardAmount> amounts)
    //     {
    //         _amounts = new(amounts);

    //         if (!_amounts.Any())
    //         {
    //             throw new ArgumentException("The amounts are empty");
    //         }

    //         if (_amounts.Any(ca => ca.Card != Card)
    //             && _amounts.Any(ca => ca.CardId != CardId))
    //         {
    //             throw new ArgumentException("All cards do not match the name");
    //         }
    //     }


    //     private readonly LinkedList<CardAmount> _amounts;

    //     private CardAmount First => _amounts.First!.Value;

    //     public string CardId => First.CardId;
    //     public Card Card => First.Card;


    //     public int Amount
    //     {
    //         get => _amounts.Sum(ca => ca.Amount);
    //         set
    //         {
    //             int change = Amount - value;
    //             while (change < 0 || change > 0 && First.Amount > 0)
    //             {
    //                 int mod = Math.Min(change, First.Amount);

    //                 First.Amount -= mod;
    //                 change -= mod;

    //                 if (First.Amount == 0)
    //                 {
    //                     // cycle amount
    //                     var firstLink = _amounts.First!;
    //                     _amounts.Remove(firstLink);
    //                     _amounts.AddLast(firstLink);
    //                 }
    //             }
    //         }
    //     }

    //     IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    //     public IEnumerator<CardAmount> GetEnumerator() => _amounts.GetEnumerator();
    // }



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

            if (_amounts.Any(ca => ca.LocationId != LocationId))
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
                int change = Amount - value;
                while (change < 0 || change > 0 && First.Amount > 0)
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



    public class ExchangeNameGroup : IEnumerable<Exchange>
    {
        public ExchangeNameGroup(IEnumerable<Exchange> exchanges)
        {
            _exchanges = new(exchanges);

            if (!_exchanges.Any())
            {
                throw new ArgumentException("The exchanges are empty");
            }

            if (_exchanges.Any(ex => ex.Card.Name != Name))
            {
                throw new ArgumentException("All cards do not match the name");
            }

            if (_exchanges.Any(ex => ex.Card.ManaCost != ManaCost))
            {
                throw new ArgumentException("All cards do not match the mana cost");
            }

            if (_exchanges.Any(ex => ex.ToId != ToId || ex.FromId != FromId))
            {
                throw new ArgumentException("All cards do not have the same location");
            }
        }

        public ExchangeNameGroup(params Exchange[] amounts)
            : this(amounts.AsEnumerable())
        { }


        // guranteed >= 1 CardAmounts in linkedlist
        private readonly LinkedList<Exchange> _exchanges;


        private Exchange First => _exchanges.First!.Value;

        public string Name => First.Card.Name;

        public string ManaCost => First.Card.ManaCost;

        public IEnumerable<string> CardIds => _exchanges.Select(ca => ca.CardId);
        public IEnumerable<Card> Cards => _exchanges.Select(ca => ca.Card);


        public int? ToId => First.ToId;
        public Deck? To => First.To;

        public int? FromId => First.FromId;
        public Deck? From => First.From;


        public int Amount
        {
            get => _exchanges.Sum(ca => ca.Amount);
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
                        var firstLink = _exchanges.First!;
                        _exchanges.Remove(firstLink);
                        _exchanges.AddLast(firstLink);
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<Exchange> GetEnumerator() => _exchanges.GetEnumerator();
    }



    /// <summary>Group of deck amounts with the same deck and same card</summary>
    public class RequestGroup
    {
        public RequestGroup(CardAmount? amount, IEnumerable<Exchange> exchanges)
        {
            _actual = amount;
            _take = exchanges.FirstOrDefault(ex => !ex.IsTrade && ex.ToId != default);
            _return = exchanges.FirstOrDefault(ex => !ex.IsTrade && ex.FromId != default);

            CheckGroup();
        }

        public RequestGroup(CardAmount? amount, params Exchange[] exchanges)
            : this(amount, exchanges.AsEnumerable())
        { }

        public RequestGroup(IEnumerable<Exchange> exchanges)
            : this(null, exchanges)
        { }

        public RequestGroup(params Exchange[] exchanges)
            : this(exchanges.AsEnumerable())
        { }


        // Guaranteed to not all be null
        private CardAmount? _actual;
        private Exchange? _take;
        private Exchange? _return;


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

        public Exchange? Take
        {
            get => _take;
            set
            {
                if (value is null || value.IsTrade || value.ToId == default)
                {
                    throw new ArgumentException("Amount is not a valid take amount");
                }

                _take = value;

                CheckGroup();
            }
        }

        public Exchange? Return
        {
            get => _return;
            set
            {
                if (value is null || value.IsTrade || value.FromId == default)
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
                || Take.CardId == cardId && Take.ToId == locationId;

            var sameRetIds = Return == null 
                || Return.CardId == cardId && Return.FromId == locationId;

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
                    && object.ReferenceEquals(Take.To, location);

            var sameRetRefs = Return == null 
                || object.ReferenceEquals(Return.Card, card)
                    && object.ReferenceEquals(Return.From, location);

            return sameActRefs && sameTakeRefs && sameRetRefs;
        }


        public string CardId =>
            Actual?.CardId
                ?? Take?.CardId
                ?? Return?.CardId
                ?? null!;

        public Card Card =>
            Actual?.Card
                ?? Take?.Card
                ?? Return?.Card
                ?? null!;


        public int LocationId =>
            Actual?.LocationId
                ?? Take?.ToId
                ?? Return?.FromId
                ?? default;

        public Location Location =>
            Actual?.Location
                ?? Take?.To
                ?? Return?.From
                ?? null!;


        public int Amount =>
            (Actual?.Amount ?? 0)
                + (Take?.Amount ?? 0)
                - (Return?.Amount ?? 0);
    }



    /// <summary>
    /// Group of card amounts with the same card name, and the same deck
    /// </summary>
    public class RequestNameGroup : IEnumerable<RequestGroup>
    {
        public RequestNameGroup(IEnumerable<CardAmount> amounts, IEnumerable<Exchange> exchanges)
        {
            // do a full outer join
            var amountTable = amounts.ToDictionary(ca => ca.CardId);
            var exchangeLookup = exchanges.ToLookup(ex => ex.CardId);

            var allCardIds = exchangeLookup
                .Select(g => g.Key)
                .Union(amountTable.Keys);

            _requestGroups = allCardIds
                .Select(cid =>
                {
                    amountTable.TryGetValue(cid, out var amount);
                    return new RequestGroup(amount, exchangeLookup[cid]);
                })
                .ToList();

            CheckGroups();
        }

        private readonly IReadOnlyList<RequestGroup> _requestGroups;


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

        public IEnumerator<RequestGroup> GetEnumerator() =>
            _requestGroups.GetEnumerator();
    }




    /// <summary>Group of trades with the same proposer and to deck</summary>
    public class TradeSet : IEnumerable<Exchange>
    {
        private IEnumerable<Exchange> _trades;

        public TradeSet(IEnumerable<Exchange> exchanges)
        {
            _trades = exchanges.ToList();

            if (!_trades.Any())
            {
                throw new ArgumentException("The trade group is empty");
            }

            if (_trades.Any(e => !e.IsTrade))
            {
                throw new ArgumentException("All exchanges are not trades");
            }

            if (_trades.Any(t => t.ToId != ToId && t.To != To))
            {
                throw new ArgumentException("All trade destinations are not the same");
            }
        }


        private Exchange First => _trades.First();

        public int ToId => (int)First.ToId!;
        public Deck To => First.To!;


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<Exchange> GetEnumerator() => _trades.GetEnumerator();
    }
}