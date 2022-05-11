using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Projections;

namespace MTGViewer.Services.Infrastructure;

public sealed class CardData
{
    public IReadOnlyList<CardUser> Users { get; set; } = Array.Empty<CardUser>();
    public IReadOnlyList<UserRef> Refs { get; set; } = Array.Empty<UserRef>();

    public IReadOnlyList<CardId> CardIds { get; set; } = Array.Empty<CardId>();
    public IReadOnlyList<Card> Cards { get; set; } = Array.Empty<Card>();

    public IReadOnlyList<Deck> Decks { get; set; } = Array.Empty<Deck>();
    public IReadOnlyList<Unclaimed> Unclaimed { get; set; } = Array.Empty<Unclaimed>();

    public IReadOnlyList<Bin> Bins { get; set; } = Array.Empty<Bin>();
    public IReadOnlyList<Excess> Excess { get; set; } = Array.Empty<Excess>();

    public IReadOnlyList<Transaction> Transactions { get; set; } = Array.Empty<Transaction>();
    public IReadOnlyList<Suggestion> Suggestions { get; set; } = Array.Empty<Suggestion>();

    // possible memory issue?

    public static async Task<CardData> FromStreamAsync(CardStream stream, CancellationToken cancel = default)
    {
        return new CardData
        {
            Users = await stream.Users
                .ToListAsync(cancel)
                .ConfigureAwait(false),

            Refs = await stream.Refs
                .ToListAsync(cancel)
                .ConfigureAwait(false),

            CardIds = await stream.CardIds
                .ToListAsync(cancel)
                .ConfigureAwait(false),

            Cards = await stream.Cards
                .ToListAsync(cancel)
                .ConfigureAwait(false),

            Decks = await stream.Decks
                .ToListAsync(cancel)
                .ConfigureAwait(false),

            Unclaimed = await stream.Unclaimed
                .ToListAsync(cancel)
                .ConfigureAwait(false),

            Bins = await stream.Bins
                .ToListAsync(cancel)
                .ConfigureAwait(false),

            Excess = await stream.Excess
                .ToListAsync(cancel)
                .ConfigureAwait(false),

            Transactions = await stream.Transactions
                .ToListAsync(cancel)
                .ConfigureAwait(false),

            Suggestions = await stream.Suggestions
                .ToListAsync(cancel)
                .ConfigureAwait(false)
        };
    }
}
