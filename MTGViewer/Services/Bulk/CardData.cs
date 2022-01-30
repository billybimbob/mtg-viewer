using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

namespace MTGViewer.Services.Internal;


public enum DataScope
{
    Default,
    Full,
    Paged,
}


public sealed class CardData
{
    public IReadOnlyList<CardUser> Users { get; set; } = Array.Empty<CardUser>();
    public IReadOnlyList<UserRef> Refs { get; set; } = Array.Empty<UserRef>();

    public IReadOnlyList<Card> Cards { get; set; } = Array.Empty<Card>();

    public IReadOnlyList<Deck> Decks { get; set; } = Array.Empty<Deck>();
    public IReadOnlyList<Unclaimed> Unclaimed { get; set; } = Array.Empty<Unclaimed>();

    public IReadOnlyList<Bin> Bins { get; set; } = Array.Empty<Bin>();

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

    public IAsyncEnumerable<Transaction> Transactions { get; set; } = AsyncEnumerable.Empty<Transaction>();
    public IAsyncEnumerable<Suggestion> Suggestions { get; set; } = AsyncEnumerable.Empty<Suggestion>();


    public static CardStream Create(CardDbContext dbContext)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        return new CardStream
        {
            Cards = dbContext.Cards
                .Include(c => c.Colors)
                .Include(c => c.Types)
                .Include(c => c.Subtypes)
                .Include(c => c.Supertypes)

                .OrderBy(c => c.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Decks = dbContext.Decks
                .Include(d => d.Cards)
                .Include(d => d.Wants)

                .OrderBy(d => d.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Unclaimed = dbContext.Unclaimed
                .Include(u => u.Cards)
                .Include(u => u.Wants)

                .OrderBy(u => u.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Bins = dbContext.Bins
                .Include(b => b.Boxes)
                    .ThenInclude(b => b.Cards)

                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable()
        };
    }


    public static CardStream Create(
        CardDbContext dbContext,
        int pageSize,
        int? page = default)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        if (pageSize < 0)
        {
            throw new ArgumentException(nameof(pageSize));
        }

        if (page < 0)
        {
            throw new ArgumentException(nameof(page));
        }

        int skip = (page ?? 0) * pageSize;

        return new CardStream
        {
            Cards = dbContext.Cards
                .Include(c => c.Colors)
                .Include(c => c.Types)
                .Include(c => c.Subtypes)
                .Include(c => c.Supertypes)

                .Skip(skip)
                .Take(pageSize)
                .OrderBy(c => c.Id)

                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Decks = dbContext.Decks
                // keep eye on, paging does not account for
                // the variable amount of Quantity entries
                .Include(d => d.Cards)
                .Include(d => d.Wants)

                .Skip(skip)
                .Take(pageSize)
                .OrderBy(d => d.Id)

                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Unclaimed = dbContext.Unclaimed
                .Include(u => u.Cards)
                .Include(u => u.Wants)

                .Skip(skip)
                .Take(pageSize)
                .OrderBy(u => u.Id)

                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Bins = dbContext.Bins
                // keep eye on, paging does not account for
                // the variable amount of Box andQuantity 
                // entries
                .Include(b => b.Boxes)
                    .ThenInclude(b => b.Cards)

                .Skip(skip)
                .Take(pageSize)
                .OrderBy(b => b.Id)

                .AsSplitQuery()
                .AsAsyncEnumerable()
        };
    }


    public static CardStream Create(
        CardDbContext dbContext,
        UserManager<CardUser> userManager)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        if (userManager is null)
        {
            throw new ArgumentNullException(nameof(userManager));
        }

        return new CardStream
        {
            Users = userManager.Users
                .OrderBy(u => u.Id)
                .AsAsyncEnumerable(),

            Refs = dbContext.Users
                .OrderBy(u => u.Id)
                .AsAsyncEnumerable(),

            Cards = dbContext.Cards
                .Include(c => c.Colors)
                .Include(c => c.Types)
                .Include(c => c.Subtypes)
                .Include(c => c.Supertypes)

                .OrderBy(c => c.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Decks = dbContext.Decks
                .Include(d => d.Cards)
                .Include(d => d.Wants)
                .Include(d => d.GiveBacks)
                .Include(d => d.TradesFrom)
                .Include(d => d.TradesTo)

                .OrderBy(d => d.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Unclaimed = dbContext.Unclaimed
                .Include(u => u.Cards)
                .Include(u => u.Wants)
                .OrderBy(u => u.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Bins = dbContext.Bins
                .Include(b => b.Boxes)
                    .ThenInclude(b => b.Cards)
                .OrderBy(b => b.Id)
                .AsSplitQuery()
                .AsAsyncEnumerable(),

            Transactions = dbContext.Transactions
                .Include(t => t.Changes)
                .OrderBy(t => t.Id)
                .AsAsyncEnumerable(),

            Suggestions = dbContext.Suggestions
                .OrderBy(s => s.Id)
                .AsAsyncEnumerable()
        };
    }


    public static async IAsyncEnumerable<int> DbSetCounts(
        CardDbContext dbContext,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancel = default)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        yield return await dbContext.Users
            .CountAsync(cancel)
            .ConfigureAwait(false);

        yield return await dbContext.Cards
            .CountAsync(cancel)
            .ConfigureAwait(false);

        yield return await dbContext.Decks
            .CountAsync(cancel)
            .ConfigureAwait(false);

        yield return await dbContext.Unclaimed
            .CountAsync(cancel)
            .ConfigureAwait(false);

        yield return await dbContext.Bins
            .CountAsync(cancel)
            .ConfigureAwait(false);
    }
}