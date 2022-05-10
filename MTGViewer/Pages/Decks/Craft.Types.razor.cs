using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Forms;

using MTGViewer.Data;

namespace MTGViewer.Pages.Decks;

public partial class Craft
{
    public enum BuildType
    {
        Holds,
        Theorycrafting
    }

    private sealed class DeckDto : ConcurrentDto
    {
        public int Id { get; init; }
        public string OwnerId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public Color Color { get; init; }

        public IEnumerable<QuantityDto> Holds { get; init; } = Enumerable.Empty<QuantityDto>();
        public IEnumerable<QuantityDto> Wants { get; init; } = Enumerable.Empty<QuantityDto>();
        public IEnumerable<QuantityDto> Givebacks { get; init; } = Enumerable.Empty<QuantityDto>();

        public IEnumerable<PageLoad> LoadedPages { get; init; } = Array.Empty<PageLoad>();

        [JsonConstructor]
        public DeckDto()
        { }

        public DeckDto(CardDbContext dbContext, DeckContext deckContext)
        {
            var deck = deckContext.Deck;

            Id = deck.Id;
            OwnerId = deck.OwnerId;

            Name = deck.Name;
            Color = deck.Color;

            Wants = deck.Wants.Select(w => new QuantityDto(dbContext, w));
            Holds = deck.Holds.Select(h => new QuantityDto(dbContext, h));
            Givebacks = deck.Givebacks.Select(g => new QuantityDto(dbContext, g));

            LoadedPages = deckContext.LoadedPages;

            dbContext.CopyToken(this, deck);
        }

        public DeckContext ToDeckContext(CardDbContext dbContext)
        {
            var deck = new Deck();

            dbContext.Entry(deck).CurrentValues.SetValues(this);

            deck.Holds.AddRange(
                Holds.Select(q => q.ToQuantity<Hold>(dbContext)));

            deck.Wants.AddRange(
                Wants.Select(q => q.ToQuantity<Want>(dbContext)));

            deck.Givebacks.AddRange(
                Givebacks.Select(q => q.ToQuantity<Giveback>(dbContext)));

            return new DeckContext(deck, LoadedPages);
        }
    }

    private sealed class QuantityDto : ConcurrentDto
    {
        public int Id { get; init; }

        public string CardId { get; init; } = string.Empty;

        public int Copies { get; init; }

        [JsonConstructor]
        public QuantityDto()
        { }

        public QuantityDto(CardDbContext dbContext, Quantity quantity)
        {
            Id = quantity.Id;
            CardId = quantity.CardId;
            Copies = quantity.Copies;

            dbContext.CopyToken(this, quantity);
        }

        public TQuantity ToQuantity<TQuantity>(CardDbContext dbContext) where TQuantity : Quantity, new()
        {
            var quantity = new TQuantity();

            dbContext.Entry(quantity).CurrentValues.SetValues(this);

            return quantity;
        }
    }

    private sealed record DeckCounts
    {
        public string OwnerId { get; init; } = string.Empty;

        public int HeldCopies { get; init; }
        public int WantCopies { get; set; }
        public int ReturnCopies { get; set; }

        public int HeldCount { get; init; }
        public int WantCount { get; set; }
        public int ReturnCount { get; set; }

        public bool HasTrades { get; init; }
    }

    private readonly record struct PageLoad(QuantityType Type, int Page);

    private sealed class DeckContext
    {
        private readonly Dictionary<Quantity, int> _originalCopies;
        private readonly Dictionary<string, QuantityGroup> _groups;

        public DeckContext(Deck deck)
        {
            ArgumentNullException.ThrowIfNull(deck);

            _originalCopies = new Dictionary<Quantity, int>();

            _groups = QuantityGroup
                .FromDeck(deck)
                .ToDictionary(qg => qg.CardId);

            Deck = deck;
            EditContext = new EditContext(deck);

            LoadedPages = new HashSet<PageLoad>();
            IsNewDeck = deck.Id == default;

            UpdateOriginals();
        }

        public DeckContext(Deck deck, IEnumerable<PageLoad> loaded) : this(deck)
        {
            foreach (var load in loaded)
            {
                LoadedPages.Add(load);
            }
        }

        public Deck Deck { get; }

        public EditContext EditContext { get; }

        public bool IsNewDeck { get; private set; }

        public ICollection<PageLoad> LoadedPages { get; }

        public IReadOnlyCollection<QuantityGroup> Groups => _groups.Values;

        public bool IsAdded(Quantity quantity)
            => !_originalCopies.ContainsKey(quantity);

        public bool IsModified(Quantity quantity)
            => quantity.Copies != _originalCopies.GetValueOrDefault(quantity);

        public bool CanSave()
        {
            if (!EditContext.Validate())
            {
                return false;
            }

            if (IsNewDeck)
            {
                return true;
            }

            if (EditContext.IsModified())
            {
                return true;
            }

            bool quantitiesModifed = _groups.Values
                .SelectMany(cg => cg)
                .Any(q => IsModified(q));

            if (quantitiesModifed)
            {
                return true;
            }

            return false;
        }

        public IEnumerable<TQuantity> GetQuantities<TQuantity>()
            where TQuantity : Quantity
        {
            var quantityType = typeof(TQuantity);

            if (quantityType == typeof(Hold))
            {
                return Deck.Holds.OfType<TQuantity>();
            }
            else if (quantityType == typeof(Want))
            {
                return Deck.Wants.OfType<TQuantity>();
            }
            // else if (quantityType == typeof(Giveback))
            else
            {
                return Deck.Givebacks.OfType<TQuantity>();
            }
        }

        public bool TryGetQuantity<TQuantity>(Card card, out TQuantity quantity)
            where TQuantity : Quantity
        {
            quantity = null!;

            if (card is null)
            {
                return false;
            }

            if (!_groups.TryGetValue(card.Id, out var group))
            {
                return false;
            }

            quantity = group.GetQuantity<TQuantity>()!;

            return quantity != null;
        }

        public void AddQuantity<TQuantity>(TQuantity quantity)
            where TQuantity : Quantity
        {
            ArgumentNullException.ThrowIfNull(quantity);

            if (!_groups.TryGetValue(quantity.CardId, out var group))
            {
                _groups.Add(quantity.CardId, new QuantityGroup(quantity));
                return;
            }

            if (group.GetQuantity<TQuantity>() is not null)
            {
                return;
            }

            group.AddQuantity(quantity);
        }

        public void AddOriginalQuantity(Quantity quantity)
        {
            ArgumentNullException.ThrowIfNull(quantity);

            AddQuantity(quantity);

            _originalCopies.Add(quantity, quantity.Copies);
        }

        public void ConvertToAddition(Quantity quantity)
        {
            ArgumentNullException.ThrowIfNull(quantity);

            _originalCopies.Remove(quantity);
        }

        private void UpdateOriginals()
        {
            var allQuantities = _groups.Values.SelectMany(qg => qg);

            foreach (var quantity in allQuantities)
            {
                _originalCopies[quantity] = quantity.Copies;
            }
        }

        public void SuccessfullySaved()
        {
            UpdateOriginals();

            IsNewDeck = false;
        }
    }
}
