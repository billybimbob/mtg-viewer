using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MTGViewer.Data
{
    /// <summary>Group of card amounts with the exact same card</summary>
    public class CardGroup : IEnumerable<CardAmount>
    {
        private readonly LinkedList<CardAmount> _amounts;

        public CardGroup(IEnumerable<CardAmount> amounts)
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

        public CardGroup(CardAmount amount, params CardAmount[] amounts)
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
            }
        }


        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<CardAmount> GetEnumerator() => _amounts.GetEnumerator();
    }



    /// <summary>Group of card amounts with the same card name</summary>
    public class CardNameGroup : IEnumerable<CardAmount>
    {
        // guranteed >= 1 CardAmounts in linkedlist
        private readonly LinkedList<CardAmount> _amounts;

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


        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();

        public IEnumerator<CardAmount> GetEnumerator() =>
            _amounts.GetEnumerator();
    }



    /// <summary>Group of deck amounts with the same deck and same card</summary>
    public class RequestGroup : IEnumerable<DeckAmount>
    {
        public RequestGroup(IEnumerable<DeckAmount> amounts)
        {
            if (!amounts.Any())
            {
                throw new ArgumentException("The group is empty");
            }

            _actual = amounts.FirstOrDefault(ca => 
                ca.Intent is Intent.None);

            _take = amounts.FirstOrDefault(ca =>
                ca.Intent is Intent.Take);

            _return = amounts.FirstOrDefault(ca =>
                ca.Intent is Intent.Return);

            CheckCorrectAmount();
        }


        public RequestGroup(params DeckAmount[] amounts)
            : this(amounts.AsEnumerable())
        { }


        // Guaranteed to not all be null
        private DeckAmount? _actual;
        private DeckAmount? _take;
        private DeckAmount? _return;


        public DeckAmount? Actual
        {
            get => _actual;
            set
            {
                if (value is null || value.Intent is not Intent.None)
                {
                    throw new ArgumentException("Amount is not a valid actual amount");
                }

                _actual = value;

                CheckCorrectAmount();
            }
        }


        public DeckAmount? Take
        {
            get => _take;
            set
            {
                if (value is null || value.Intent is not Intent.Take)
                {
                    throw new ArgumentException("Amount is not a valid take amount");
                }

                _take = value;

                CheckCorrectAmount();
            }
        }


        public DeckAmount? Return
        {
            get => _return;
            set
            {
                if (value is null || value.Intent is not Intent.Return)
                {
                    throw new ArgumentException("Amount is not a valid return amount");
                }

                _return = value;

                CheckCorrectAmount();
            }
        }


        private void CheckCorrectAmount()
        {
            if (this.Count() == 1)
            {
                return;
            }

            var cardId = CardId;
            var locationId = LocationId;

            var idsSame = this.All(da =>
                da.CardId == cardId && da.LocationId == locationId);

            var card = Card;
            var location = Location;

            var refsSame = this.All(da =>
                object.ReferenceEquals(da.Card, card)
                    && object.ReferenceEquals(da.Location, location));

            if (!idsSame && !refsSame)
            {
                throw new ArgumentException(
                    "Pairs do not reference the same location or card");
            }
        }


        public string CardId =>
            this.Select(da => da.CardId).First();

        public Card Card =>
            this.Select(da => da.Card).First();


        public int LocationId =>
            this.Select(da => da.LocationId).First();

        public Location Location =>
            this.Select(da => da.Location).First();


        public int Amount =>
            (Actual?.Amount ?? 0)
                + (Take?.Amount ?? 0)
                - (Return?.Amount ?? 0);


        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();


        public IEnumerator<DeckAmount> GetEnumerator()
        {
            if (Actual is not null)
            {
                yield return Actual;
            }

            if (Take is not null)
            {
                yield return Take;
            }

            if (Return is not null)
            {
                yield return Return;
            }
        }
    }



    /// <summary>
    /// Group of card amounts with the same card name, and the same deck
    /// </summary>
    public class RequestNameGroup : IEnumerable<DeckAmount>
    {
        public RequestNameGroup(IEnumerable<DeckAmount> amounts)
        {
            if (!amounts.Any())
            {
                throw new ArgumentException("The group is empty");
            }

            var actuals = amounts.Where(da => da.Intent is Intent.None);
            var takes   = amounts.Where(da => da.Intent is Intent.Take);
            var returns = amounts.Where(da => da.Intent is Intent.Return);

            if (actuals.Any())
            {
                Actuals = new(actuals);
            }

            if (takes.Any())
            {
                Takes = new(takes);
            }

            if (returns.Any())
            {
                Returns = new(returns);
            }

            CheckCorrectPair();
        }


        public RequestNameGroup(params DeckAmount[] amounts)
            : this(amounts.AsEnumerable())
        { }


        // Guaranteed to not all be null
        public CardNameGroup? Actuals { get; }
        public CardNameGroup? Takes { get; }
        public CardNameGroup? Returns { get; }


        private void CheckCorrectPair()
        {
            var name = Name;
            var locationId = LocationId;
            var location = Location;

            var nameSame = AllGroups(cg => cg.Name == name);
            var idSame = AllGroups(cg => cg.LocationId == locationId);
            var refSame = AllGroups(cg => object.ReferenceEquals(cg.Location, location));

            if (!nameSame || !idSame && !refSame)
            {
                throw new ArgumentException(
                    "Pairs do not reference the same location or card");
            }
        }


        private bool AllGroups(Func<CardNameGroup, bool> groupPredicate)
        {
            return (Actuals is null || groupPredicate(Actuals))
                && (Takes is null || groupPredicate(Takes))
                && (Returns is null || groupPredicate(Returns));
        }


        public string Name =>
            Actuals?.Name
                ?? Takes?.Name
                ?? Returns?.Name
                ?? null!;

        public Location Location =>
            Actuals?.Location
                ?? Takes?.Location
                ?? Returns?.Location
                ?? null!;

        public int LocationId =>
            Actuals?.LocationId
                ?? Takes?.LocationId
                ?? Returns?.LocationId
                ?? default;

        public int Amount =>
            (Actuals?.Amount ?? 0)
                + (Takes?.Amount ?? 0)
                - (Returns?.Amount ?? 0);


        public IEnumerable<string> CardIds =>
            this.Select(da => da.CardId);

        public IEnumerable<Card> Cards =>
            this.Select(da => da.Card);
        

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        public IEnumerator<DeckAmount> GetEnumerator()
        {
            var amounts = new List<DeckAmount>();

            if (Actuals is not null)
            {
                amounts.AddRange( Actuals.Cast<DeckAmount>() );
            }

            if (Takes is not null)
            {
                amounts.AddRange( Takes.Cast<DeckAmount>() );
            }

            if (Returns is not null)
            {
                amounts.AddRange( Returns.Cast<DeckAmount>() );
            }

            return amounts.GetEnumerator();
        }
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