using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Components.Forms;

namespace MtgViewer.Data.Infrastructure;

public sealed class DeckContext
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

        IsNewDeck = deck.Id == default;

        UpdateOriginals();
    }

    public Deck Deck { get; }

    public EditContext EditContext { get; }

    public bool IsNewDeck { get; private set; }

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

        bool quantitiesModified = _groups.Values
            .SelectMany(cg => cg)
            .Any(q => IsModified(q));

        if (quantitiesModified)
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

    public void AddOriginalQuantity<TQuantity>(TQuantity quantity)
        where TQuantity : Quantity
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
        foreach (var group in _groups.Values)
        {
            foreach (var quantity in group)
            {
                _originalCopies[quantity] = quantity.Copies;
            }
        }
    }

    public void SuccessfullySaved()
    {
        UpdateOriginals();

        IsNewDeck = false;
    }
}
