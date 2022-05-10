using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Utils;

namespace MTGViewer.Pages.Decks;

public partial class Craft
{
    internal async Task CommitChangesAsync()
    {
        if (_isBusy
            || _deckContext is null
            || !_deckContext.CanSave())
        {
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            Result = await SaveOrConcurrentRecoverAsync(dbContext);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning("{Error}", ex);

            Result = SaveResult.Error;
        }
        catch (DbUpdateException ex)
        {
            Logger.LogError("{Error}", ex);

            Result = SaveResult.Error;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task<SaveResult> SaveOrConcurrentRecoverAsync(CardDbContext dbContext)
    {
        if (_deckContext is null)
        {
            return SaveResult.Error;
        }

        try
        {
            dbContext.Cards.AttachRange(_cards);

            PrepareChanges(dbContext, _deckContext);

            await dbContext.SaveChangesAsync(_cancel.Token);

            _deckContext.SuccessfullySaved();

            return SaveResult.Success;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await UpdateDeckFromDbAsync(dbContext, _deckContext, ex, _cancel.Token);

            _cards.UnionWith(dbContext.Cards.Local);

            return SaveResult.Error;
        }
    }

    private static void PrepareChanges(CardDbContext dbContext, DeckContext deckContext)
    {
        var deck = deckContext.Deck;

        if (deckContext.IsNewDeck)
        {
            dbContext.Decks.Add(deck);
        }
        else
        {
            dbContext.Decks.Attach(deck).State = EntityState.Modified;
        }

        PrepareQuantity(deckContext, dbContext.Holds);
        PrepareQuantity(deckContext, dbContext.Wants);
        PrepareQuantity(deckContext, dbContext.Givebacks);
    }

    private static void PrepareQuantity<TQuantity>(
        DeckContext deckContext,
        DbSet<TQuantity> dbQuantities)
        where TQuantity : Quantity
    {
        foreach (var quantity in deckContext.GetQuantities<TQuantity>())
        {
            bool isEmpty = quantity.Copies == 0;
            bool isTracked = dbQuantities.Local.Contains(quantity);

            if (!isEmpty && deckContext.IsAdded(quantity))
            {
                dbQuantities.Add(quantity);
            }
            else if (isEmpty && isTracked)
            {
                dbQuantities.Remove(quantity);
            }
            else if (!isEmpty && !isTracked)
            {
                dbQuantities.Attach(quantity);
            }
            else if (deckContext.IsModified(quantity))
            {
                dbQuantities.Attach(quantity).State = EntityState.Modified;
            }
        }
    }

    private static async Task UpdateDeckFromDbAsync(
        CardDbContext dbContext,
        DeckContext deckContext,
        DbUpdateConcurrencyException ex,
        CancellationToken cancel)
    {
        if (HasNoDeckConflicts(deckContext, ex))
        {
            return;
        }

        if (cancel.IsCancellationRequested)
        {
            return;
        }

        Deck? dbDeck = null;
        var localDeck = deckContext.Deck;

        try
        {
            dbDeck = await dbContext.Decks
                .Include(d => d.Holds) // unbounded: keep eye on
                    .ThenInclude(h => h.Card)
                .Include(d => d.Wants) // unbounded: keep eye on
                    .ThenInclude(w => w.Card)
                .Include(d => d.Givebacks) // unbounded: keep eye on
                    .ThenInclude(g => g.Card)

                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(d => d.Id == localDeck.Id, cancel);
        }
        catch (OperationCanceledException)
        { }

        if (cancel.IsCancellationRequested)
        {
            return;
        }

        if (dbDeck == default)
        {
            return;
        }

        MergeDbRemoves(deckContext, dbDeck);
        MergeDbConflicts(dbContext, deckContext, dbDeck);
        MergeDbAdditions(dbContext, deckContext, dbDeck);

        CapGivebacks(deckContext.Groups);

        dbContext.MatchToken(localDeck, dbDeck);
    }

    private static bool HasNoDeckConflicts(
        DeckContext deckContext,
        DbUpdateConcurrencyException ex)
    {
        var deckConflict = ex.Entries<Deck>().SingleOrDefault();

        if (deckConflict is not null && deckConflict.Entity.Id == deckContext.Deck.Id)
        {
            return false;
        }

        var cardIds = deckContext.Groups
            .Select(cg => cg.CardId)
            .ToList();

        var holdConflicts = ex.Entries<Hold>()
            .IntersectBy(cardIds, e => e.Entity.CardId);

        var wantConflicts = ex.Entries<Want>()
            .IntersectBy(cardIds, e => e.Entity.CardId);

        var giveConflicts = ex.Entries<Giveback>()
            .IntersectBy(cardIds, e => e.Entity.CardId);

        return !holdConflicts.Any()
            && !wantConflicts.Any()
            && !giveConflicts.Any();
    }

    private static void MergeDbRemoves(DeckContext deckContext, Deck dbDeck)
    {
        MergeRemovedQuantity(deckContext, dbDeck.Holds);

        MergeRemovedQuantity(deckContext, dbDeck.Wants);

        MergeRemovedQuantity(deckContext, dbDeck.Givebacks);
    }

    private static void MergeRemovedQuantity<TQuantity>(
        DeckContext deckContext,
        IReadOnlyList<TQuantity> dbQuantities)
        where TQuantity : Quantity
    {
        var removedQuantities = deckContext
            .GetQuantities<TQuantity>()

            .GroupJoin(dbQuantities,
                lq => (lq.CardId, lq.LocationId),
                db => (db.CardId, db.LocationId),
                (local, dbs) =>
                    (local, noDb: !dbs.Any()))

            .Where(ln => ln.noDb)
            .Select(ln => ln.local);

        foreach (var removedQuantity in removedQuantities)
        {
            if (deckContext.IsModified(removedQuantity))
            {
                deckContext.ConvertToAddition(removedQuantity);
            }
            else
            {
                removedQuantity.Copies = 0;
            }
        }
    }

    private static void MergeDbConflicts(
        CardDbContext dbContext,
        DeckContext deckContext,
        Deck dbDeck)
    {
        MergeQuantityConflict(dbContext, deckContext, dbDeck.Holds);

        MergeQuantityConflict(dbContext, deckContext, dbDeck.Wants);

        MergeQuantityConflict(dbContext, deckContext, dbDeck.Givebacks);
    }

    private static void MergeQuantityConflict<TQuantity>(
        CardDbContext dbContext,
        DeckContext deckContext,
        IEnumerable<TQuantity> dbQuantities)
        where TQuantity : Quantity
    {
        foreach (var dbQuantity in dbQuantities)
        {
            if (!deckContext.TryGetQuantity(dbQuantity.Card, out TQuantity localQuantity))
            {
                continue;
            }

            if (!deckContext.IsModified(localQuantity))
            {
                localQuantity.Copies = dbQuantity.Copies;
            }

            dbContext.MatchToken(localQuantity, dbQuantity);
        }
    }

    private static void MergeDbAdditions(
        CardDbContext dbContext,
        DeckContext deckContext,
        Deck dbDeck)
    {
        MergeNewQuantity(dbContext, deckContext, dbDeck.Holds);

        MergeNewQuantity(dbContext, deckContext, dbDeck.Wants);

        MergeNewQuantity(dbContext, deckContext, dbDeck.Givebacks);
    }

    private static void MergeNewQuantity<TQuantity>(
        CardDbContext dbContext,
        DeckContext deckContext,
        IReadOnlyList<TQuantity> dbQuantities)
        where TQuantity : Quantity, new()
    {
        foreach (var dbQuantity in dbQuantities)
        {
            if (deckContext.TryGetQuantity(dbQuantity.Card, out TQuantity _))
            {
                continue;
            }

            var card = dbContext.Cards.Local
                .FirstOrDefault(c => c.Id == dbQuantity.CardId);

            if (card == default)
            {
                card = dbQuantity.Card;

                card.Holds.Clear();
                card.Wants.Clear();

                dbContext.Cards.Attach(card);
            }

            var newQuantity = new TQuantity
            {
                Id = dbQuantity.Id,
                Card = card,
                Location = deckContext.Deck,
                Copies = dbQuantity.Copies
            };

            // attach for nav fixup

            dbContext.Set<TQuantity>().Attach(newQuantity);

            dbContext.MatchToken(newQuantity, dbQuantity);

            deckContext.AddOriginalQuantity(newQuantity);
        }
    }

    private static void CapGivebacks(IEnumerable<QuantityGroup> deckCards)
    {
        foreach (var cardGroup in deckCards)
        {
            if (cardGroup.Giveback is null)
            {
                continue;
            }

            int currentReturn = cardGroup.Giveback.Copies;
            int copiesCap = cardGroup.Hold?.Copies ?? currentReturn;

            cardGroup.Giveback.Copies = Math.Min(currentReturn, copiesCap);
        }
    }
}
