using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MTGViewer.Data
{
    public class SameNameGroup : IEnumerable<CardAmount>
    {
        // guranteed >= 1 CardAmounts in linkedlist
        private readonly LinkedList<CardAmount> _amounts;

        public SameNameGroup(IEnumerable<CardAmount> amounts)
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

        public SameNameGroup(params CardAmount[] amounts) : this(amounts.AsEnumerable())
        { }


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
                while (change != 0 && First.Amount > 0)
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



    public class SameNamePair : IEnumerable<DeckAmount>
    {
        public SameNamePair(IEnumerable<DeckAmount> amounts)
        {
            if (!amounts.Any())
            {
                throw new ArgumentException("The group is empty");
            }

            var actuals = amounts.Where(ca => !ca.IsRequest);
            var requests = amounts.Where(ca => ca.IsRequest);

            if (actuals.Any())
            {
                Actuals = new(actuals);
            }

            if (requests.Any())
            {
                Requests = new(requests);
            }

            CheckCorrectPair();
        }


        public SameNamePair(params DeckAmount[] amounts) : this(amounts.AsEnumerable())
        { }


        // Applied and Request are guaranteed to not both be null
        public SameNameGroup? Actuals { get; } // private set; }
        public SameNameGroup? Requests { get; } //private set; }


        private void CheckCorrectPair()
        {
            if (Actuals is null || Requests is null)
            {
                return;
            }

            var nameSame = Actuals.Name == Requests.Name;
            var idSame = Actuals.LocationId == Requests.LocationId;
            var refSame = object.ReferenceEquals(Actuals.Location, Requests.Location);

            if (!nameSame || !idSame && !refSame)
            {
                throw new ArgumentException(
                    "Pairs do not reference the same location or card");
            }
        }


        public string Name =>
            Actuals?.Name ?? Requests?.Name ?? null!;

        public Location Location =>
            Actuals?.Location ?? Requests?.Location ?? null!;

        public int Amount =>
            (Actuals?.Amount ?? 0) + (Requests?.Amount ?? 0);


        public IEnumerable<string> CardIds =>
            (Actuals?.CardIds ?? Enumerable.Empty<string>())
                .Concat(Requests?.CardIds ?? Enumerable.Empty<string>());

        public IEnumerable<Card> Cards =>
            (Actuals?.Cards ?? Enumerable.Empty<Card>())
                .Concat(Requests?.Cards ?? Enumerable.Empty<Card>());
        

        // public void Add(CardAmount amount)
        // {
        //     if (amount.IsRequest)
        //     {
        //         if (Actuals is null)
        //         {
        //             Actuals = new NameGroup(amount);
        //         }
        //         else
        //         {
        //             Actuals.Add(amount);
        //         }
        //     } 
        //     else
        //     {
        //         if (Requests is null)
        //         {
        //             Requests = new NameGroup(amount);
        //         }
        //         else
        //         {
        //             Requests.Add(amount);
        //         }
        //     }
        // }


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<DeckAmount> GetEnumerator()
        {
            var amounts = Enumerable.Empty<DeckAmount>();

            if (Actuals is not null)
            {
                amounts = amounts.Concat(
                    Actuals.Cast<DeckAmount>());
            }

            if (Requests is not null)
            {
                amounts = amounts.Concat(
                    Requests.Cast<DeckAmount>());
            }

            return amounts.GetEnumerator();
        }
    }



    public class SameCardGroup : IEnumerable<CardAmount>
    {
        private readonly LinkedList<CardAmount> _amounts;

        public SameCardGroup(IEnumerable<CardAmount> amounts)
        {
            _amounts = new(amounts);

            if (!_amounts.Any())
            {
                throw new ArgumentException("The amounts are empty");
            }

            if (_amounts.Any(ca => ca.Card != Card)
                && _amounts.Any(ca => ca.CardId != CardId))
            {
                throw new ArgumentException("All cards do not match the name");
            }
        }

        public SameCardGroup(CardAmount amount, params CardAmount[] amounts)
            : this(amounts.Prepend(amount))
        { }

        private CardAmount First => _amounts.First!.Value;


        public string CardId => First.CardId;
        public Card Card => First.Card;


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

                if (change > 0) // all amounts are zero
                {
                    First.Amount -= change;
                }
            }
        }


        public void Add(CardAmount amount)
        {
            if (amount.Card.Name != Card.Name)
            {
                return;
            }

            _amounts.AddLast(amount);
        }


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<CardAmount> GetEnumerator() => _amounts.GetEnumerator();
    }



    public class SameAmountPair : IEnumerable<DeckAmount>
    {
        public SameAmountPair(DeckAmount amount1, DeckAmount? amount2 = null)
        {
            if (amount1.IsRequest && (amount2?.IsRequest ?? false))
            {
                throw new ArgumentException("Pair cannot both be requests");
            }

            if (!amount1.IsRequest && !(amount2?.IsRequest ?? true))
            {
                throw new ArgumentException("Pair must have one request");
            }

            Actual = amount1.IsRequest ? amount2 : amount1;
            Request = amount1.IsRequest ? amount1 : amount2;

            CheckCorrectPair();
        }


        public SameAmountPair(IEnumerable<DeckAmount> amounts)
        {
            if (!amounts.Any())
            {
                throw new ArgumentException("The group is empty");
            }

            Actual = amounts.FirstOrDefault(ca => !ca.IsRequest);
            Request = amounts.FirstOrDefault(ca => ca.IsRequest);

            CheckCorrectPair();
        }


        // Applied and Request are guaranteed to not both be null
        private DeckAmount? _actual;
        private DeckAmount? _request;


        public DeckAmount? Actual
        {
            get => _actual;
            set
            {
                if (value is null)
                {
                    return;
                }

                _actual = value;

                CheckCorrectPair();
            }
        }


        public DeckAmount? Request
        {
            get => _request;
            set
            {
                if (value is null)
                {
                    return;
                }

                _request = value;

                CheckCorrectPair();
            }
        }


        private void CheckCorrectPair()
        {
            if (Actual is null || Request is null)
            {
                return;
            }

            var idsSame = Actual.CardId == Request.CardId
                && Actual.LocationId == Request.LocationId;

            var refsSame = object.ReferenceEquals(Actual.Card, Request.Card)
                && object.ReferenceEquals(Actual.Location, Request.Location);

            if (!idsSame && !refsSame)
            {
                throw new ArgumentException(
                    "Pairs do not reference the same location or card");
            }
        }


        public string CardId => 
            Actual?.CardId ?? Request?.CardId ?? null!;

        public Card Card =>
            Actual?.Card ?? Request?.Card ?? null!;

        public Location Location =>
            Actual?.Location ?? Request?.Location ?? null!;

        public int Amount =>
            (Actual?.Amount ?? 0) + (Request?.Amount ?? 0);


        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        public IEnumerator<DeckAmount> GetEnumerator()
        {
            if (Actual is not null)
            {
                yield return Actual;
            }

            if (Request is not null)
            {
                yield return Request;
            }
        }
    }



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

            if (_trades.Any(t => t.ProposerId != ProposerId && t.Proposer != Proposer))
            {
                throw new ArgumentException("All proposers are not the same");
            }

            if (_trades.Any(t => t.ToId != ToId && t.To != To))
            {
                throw new ArgumentException("All trade destinations are not the same");
            }
        }


        private Trade First => _trades.First();

        public string ProposerId => First.ProposerId;
        public UserRef Proposer => First.Proposer;

        public int ToId => First.ToId;
        public Deck To => First.To;


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<Trade> GetEnumerator() => _trades.GetEnumerator();
    }
}