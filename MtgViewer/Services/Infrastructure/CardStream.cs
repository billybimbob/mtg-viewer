using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Projections;

namespace MtgViewer.Services.Infrastructure;

public sealed class CardStream
{
    public IAsyncEnumerable<CardUser> Users { get; set; } = AsyncEnumerable.Empty<CardUser>();
    public IAsyncEnumerable<Player> Players { get; set; } = AsyncEnumerable.Empty<Player>();

    public IAsyncEnumerable<CardId> CardIds { get; set; } = AsyncEnumerable.Empty<CardId>();
    public IAsyncEnumerable<Card> Cards { get; set; } = AsyncEnumerable.Empty<Card>();

    public IAsyncEnumerable<Deck> Decks { get; set; } = AsyncEnumerable.Empty<Deck>();
    public IAsyncEnumerable<Unclaimed> Unclaimed { get; set; } = AsyncEnumerable.Empty<Unclaimed>();

    public IAsyncEnumerable<Bin> Bins { get; set; } = AsyncEnumerable.Empty<Bin>();
    public IAsyncEnumerable<Excess> Excess { get; set; } = AsyncEnumerable.Empty<Excess>();

    public IAsyncEnumerable<Transaction> Transactions { get; set; } = AsyncEnumerable.Empty<Transaction>();
    public IAsyncEnumerable<Suggestion> Suggestions { get; set; } = AsyncEnumerable.Empty<Suggestion>();

    public static CardStream Default(CardDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return new CardStream
        {
            CardIds = dbContext.Cards
                .OrderBy(c => c.Id)
                .Select(c => new CardId
                {
                    Id = c.Id,
                    MultiverseId = c.MultiverseId
                })
                .AsAsyncEnumerable(),

            Decks = dbContext.Decks
                .Include(d => d.Holds
                    .OrderBy(h => h.Id))

                .Include(d => d.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(d => d.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Unclaimed = dbContext.Unclaimed
                .Include(u => u.Holds
                    .OrderBy(h => h.Id))

                .Include(u => u.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(u => u.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Bins = dbContext.Bins
                .Include(b => b.Boxes
                    .OrderBy(b => b.Id))
                    .ThenInclude(b => b.Holds)

                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Excess = dbContext.Excess
                .Include(e => e.Holds
                    .OrderBy(h => h.Id))
                .OrderBy(e => e.Id)
                .AsAsyncEnumerable(),

            Suggestions = dbContext.Suggestions
                .OrderBy(s => s.Id)
                .AsAsyncEnumerable()
        };
    }

    public static CardStream User(CardDbContext dbContext, string userId)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(userId);

        return new CardStream
        {
            CardIds = dbContext.Cards
                .Where(c =>
                    c.Holds
                        .Any(h => h.Location is Deck
                            && (h.Location as Deck)!.OwnerId == userId)
                    || c.Wants
                        .Any(w => w.Location is Deck
                            && (w.Location as Deck)!.OwnerId == userId)
                    || c.Suggestions
                        .Any(s => s.ReceiverId == userId))

                .OrderBy(c => c.Id)
                .Select(c => new CardId
                {
                    Id = c.Id,
                    MultiverseId = c.MultiverseId
                })
                .AsAsyncEnumerable(),

            Decks = dbContext.Decks
                .Where(d => d.OwnerId == userId)

                .Include(d => d.Holds
                    .OrderBy(h => h.Id))

                .Include(d => d.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(d => d.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Suggestions = dbContext.Suggestions
                .Where(s => s.ReceiverId == userId)
                .OrderBy(s => s.Id)
                .AsAsyncEnumerable()
        };
    }

    public static CardStream Treasury(CardDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return new CardStream
        {
            CardIds = dbContext.Cards
                .Where(c => c.Holds
                    .Any(h => h.Location is Box
                        || h.Location is Excess
                        || h.Location is Unclaimed))

                .OrderBy(c => c.Id)
                .Select(c => new CardId
                {
                    Id = c.Id,
                    MultiverseId = c.MultiverseId
                })
                .AsAsyncEnumerable(),

            Unclaimed = dbContext.Unclaimed
                .Include(u => u.Holds
                    .OrderBy(h => h.Id))

                .Include(u => u.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(u => u.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Bins = dbContext.Bins
                .Include(b => b.Boxes
                    .OrderBy(b => b.Id))
                    .ThenInclude(b => b.Holds
                        .OrderBy(h => h.Id))

                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Excess = dbContext.Excess
                .Include(e => e.Holds
                    .OrderBy(h => h.Id))
                .OrderBy(e => e.Id)
                .AsAsyncEnumerable(),
        };
    }

    public static CardStream Reset(CardDbContext dbContext)
    {
        return new CardStream
        {
            Cards = dbContext.Cards
                .Include(c => c.Flip)
                .OrderBy(c => c.Id)
                .AsAsyncEnumerable(),

            Decks = dbContext.Decks
                .Include(d => d.Holds
                    .OrderBy(h => h.Id))

                .Include(d => d.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(d => d.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Unclaimed = dbContext.Unclaimed
                .Include(u => u.Holds
                    .OrderBy(h => h.Id))

                .Include(u => u.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(u => u.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Bins = dbContext.Bins
                .Include(b => b.Boxes
                    .OrderBy(b => b.Id))
                    .ThenInclude(b => b.Holds)

                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Excess = dbContext.Excess
                .Include(e => e.Holds
                    .OrderBy(h => h.Id))
                .OrderBy(e => e.Id)
                .AsAsyncEnumerable(),

            Suggestions = dbContext.Suggestions
                .OrderBy(s => s.Id)
                .AsAsyncEnumerable()
        };
    }

    public static CardStream All(CardDbContext dbContext, UserManager<CardUser> userManager)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(userManager);

        return new CardStream
        {
            Users = userManager.Users
                .OrderBy(u => u.Id)
                .AsAsyncEnumerable(),

            Players = dbContext.Players
                .OrderBy(p => p.Id)
                .AsAsyncEnumerable(),

            Cards = dbContext.Cards
                .Include(c => c.Flip)
                .OrderBy(c => c.Id)
                .AsAsyncEnumerable(),

            Decks = dbContext.Decks
                .Include(d => d.Holds
                    .OrderBy(h => h.Id))

                .Include(d => d.Wants
                    .OrderBy(w => w.Id))

                .Include(d => d.Givebacks
                    .OrderBy(g => g.Id))

                .Include(d => d.TradesFrom
                    .OrderBy(t => t.Id))

                .Include(d => d.TradesTo
                    .OrderBy(t => t.Id))

                .OrderBy(d => d.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Unclaimed = dbContext.Unclaimed
                .Include(u => u.Holds
                    .OrderBy(h => h.Id))

                .Include(u => u.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(u => u.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Bins = dbContext.Bins
                .Include(b => b.Boxes
                    .OrderBy(b => b.Id))
                    .ThenInclude(b => b.Holds
                        .OrderBy(h => h.Id))

                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Excess = dbContext.Excess
                .Include(e => e.Holds
                    .OrderBy(h => h.Id))
                .OrderBy(e => e.Id)
                .AsAsyncEnumerable(),

            Transactions = dbContext.Transactions
                .Include(t => t.Changes
                    .OrderBy(c => c.Id))
                .OrderBy(t => t.Id)
                .AsAsyncEnumerable(),

            Suggestions = dbContext.Suggestions
                .OrderBy(s => s.Id)
                .AsAsyncEnumerable()
        };
    }
}
