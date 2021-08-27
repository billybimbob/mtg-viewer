using System;
using System.Collections;
using System.Collections.Generic;

#nullable enable

namespace MTGViewer.Data
{
    public enum SaveResult
    {
        None,
        Success,
        Error
    }


    public class AmountGroup : IEnumerable<CardAmount>
    {
        private CardAmount? _request;

        public AmountGroup(CardAmount applied, CardAmount? request = null)
        {
            Applied = applied;
            Request = request;
        }

        public CardAmount Applied { get; }

        public CardAmount? Request
        {
            get => _request;
            set
            {
                if (value is not null
                    && (!object.ReferenceEquals(Applied.Card, value.Card)
                        || !object.ReferenceEquals(Applied.Location, value.Location)))
                {
                    throw new ArgumentException("Card ids are not the same");
                }

                _request = value;
            }
        }

        public string CardId => Applied.Card?.Id
            ?? Request?.Card?.Id
            ?? Applied.CardId;

        public Card Card
        {
            get => Applied.Card;
        }

        public Location Location
        {
            get => Applied.Location;
        }

        public int Amount =>
            Applied.Amount + (Request?.Amount ?? 0);


        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        public IEnumerator<CardAmount> GetEnumerator()
        {
            yield return Applied;

            if (Request is not null)
            {
                yield return Request;
            }
        }
    }
}