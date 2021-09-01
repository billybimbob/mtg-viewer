using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MTGViewer.Data
{
    public class AmountPair : IEnumerable<CardAmount>
    {
        public AmountPair(CardAmount amount1, CardAmount? amount2 = null)
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


        public AmountPair(IEnumerable<CardAmount> amounts)
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
        private CardAmount? _actual;
        private CardAmount? _request;


        public CardAmount? Actual
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


        public CardAmount? Request
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


        public IEnumerator<CardAmount> GetEnumerator()
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