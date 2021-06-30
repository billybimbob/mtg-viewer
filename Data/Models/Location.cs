using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data.Concurrency;

#nullable enable

namespace MTGViewer.Data
{
    public class Location : Concurrent
    {
        public Location(string name)
        {
            Name = name;
        }

        public int Id { get; set; }

        public string Name { get; set; }

        public string? OwnerId { get; set; }
        public CardUser? Owner { get; set; }

        public ICollection<CardAmount> Cards { get; } = new HashSet<CardAmount>();

        public bool IsShared => OwnerId == default;
    }

    public class CardAmount : Concurrent
    {
        public string CardId { get; set; } = null!;
        public Card Card { get; set; } = null!;

        public int LocationId { get; set; }
        public Location Location { get; set; } = null!;

        public bool IsRequest { get; set; }

        [Range(0, int.MaxValue)]
        public int Amount { get; set; }
    }


    [NotMapped]
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
                if (value != null
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
            set
            {
                if (value == null)
                {
                    return;
                }

                foreach (var amount in this)
                {
                    amount.Card = value;
                }
            }
        }

        public Location Location
        {
            get => Applied.Location;
            set
            {
                foreach(var amount in this)
                {
                    amount.Location = value;
                }
            }
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

            if (Request != null)
            {
                yield return Request;
            }
        }
    }

}