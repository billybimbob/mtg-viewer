using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

namespace MTGViewer.Services.Internal;


public sealed class CardData
{
    public IReadOnlyList<CardUser> Users { get; set; } = Array.Empty<CardUser>();
    public IReadOnlyList<UserRef> Refs { get; set; } = Array.Empty<UserRef>();

    public IReadOnlyList<Card> Cards { get; set; } = Array.Empty<Card>();

    public IReadOnlyList<Deck> Decks { get; set; } = Array.Empty<Deck>();
    public IReadOnlyList<Unclaimed> Unclaimed { get; set; } = Array.Empty<Unclaimed>();

    public IReadOnlyList<Bin> Bins { get; set; } = Array.Empty<Bin>();
    public IReadOnlyList<Excess> Excess { get; set; } = Array.Empty<Excess>();

    public IReadOnlyList<Transaction> Transactions { get; set; } = Array.Empty<Transaction>();
    public IReadOnlyList<Suggestion> Suggestions { get; set; } = Array.Empty<Suggestion>();
    public IReadOnlyList<Trade> Trades { get; set; } = Array.Empty<Trade>();
}


public sealed class CardStream
{
    public IAsyncEnumerable<CardUser> Users { get; set; } = AsyncEnumerable.Empty<CardUser>();
    public IAsyncEnumerable<UserRef> Refs { get; set; } = AsyncEnumerable.Empty<UserRef>();

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
            Cards = dbContext.Cards
                .Include(c => c.Flip)
                .OrderBy(c => c.Id)
                .AsAsyncEnumerable(),

            Decks = dbContext.Decks
                .Include(d => d.Cards
                    .OrderBy(a => a.Id))

                .Include(d => d.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(d => d.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Unclaimed = dbContext.Unclaimed
                .Include(u => u.Cards
                    .OrderBy(a => a.Id))

                .Include(u => u.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(u => u.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Bins = dbContext.Bins
                .Include(b => b.Boxes
                    .OrderBy(b => b.Id))
                    .ThenInclude(b => b.Cards)

                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Excess = dbContext.Excess
                .Include(e => e.Cards
                    .OrderBy(a => a.Id))
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
            Cards = dbContext.Cards
                .Where(c =>
                    c.Amounts
                        .Any(a => a.Location is Deck
                            && (a.Location as Deck)!.OwnerId == userId)
                    || c.Wants
                        .Any(w => w.Location is Deck
                            && (w.Location as Deck)!.OwnerId == userId)
                    || c.Suggestions
                        .Any(s => s.ReceiverId == userId))

                .Include(c => c.Flip)
                .OrderBy(c => c.Id)
                .AsAsyncEnumerable(),

            Decks = dbContext.Decks
                .Where(d => d.OwnerId == userId)

                .Include(d => d.Cards
                    .OrderBy(a => a.Id))

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
            Cards = dbContext.Cards
                .Where(c => c.Amounts
                    .Any(a => a.Location is Box
                        || a.Location is Excess
                        || a.Location is Unclaimed))
                .Include(c => c.Flip)
                .OrderBy(c => c.Id)
                .AsAsyncEnumerable(),

            Unclaimed = dbContext.Unclaimed
                .Include(u => u.Cards
                    .OrderBy(a => a.Id))

                .Include(u => u.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(u => u.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Bins = dbContext.Bins
                .Include(b => b.Boxes
                    .OrderBy(b => b.Id))
                    .ThenInclude(b => b.Cards
                        .OrderBy(a => a.Id))

                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Excess = dbContext.Excess
                .Include(e => e.Cards
                    .OrderBy(a => a.Id))
                .OrderBy(e => e.Id)
                .AsAsyncEnumerable(),
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

            Refs = dbContext.Users
                .OrderBy(u => u.Id)
                .AsAsyncEnumerable(),

            Cards = dbContext.Cards
                .Include(c => c.Flip)
                .OrderBy(c => c.Id)
                .AsAsyncEnumerable(),

            Decks = dbContext.Decks
                .Include(d => d.Cards
                    .OrderBy(a => a.Id))

                .Include(d => d.Wants
                    .OrderBy(w => w.Id))

                .Include(d => d.GiveBacks
                    .OrderBy(g => g.Id))

                .Include(d => d.TradesFrom
                    .OrderBy(t => t.Id))

                .Include(d => d.TradesTo
                    .OrderBy(t => t.Id))

                .OrderBy(d => d.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Unclaimed = dbContext.Unclaimed
                .Include(u => u.Cards
                    .OrderBy(a => a.Id))

                .Include(u => u.Wants
                    .OrderBy(w => w.Id))

                .OrderBy(u => u.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Bins = dbContext.Bins
                .Include(b => b.Boxes
                    .OrderBy(b => b.Id))
                    .ThenInclude(b => b.Cards
                        .OrderBy(a => a.Id))

                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Excess = dbContext.Excess
                .Include(e => e.Cards
                    .OrderBy(a => a.Id))
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