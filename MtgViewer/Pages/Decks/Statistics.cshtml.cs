using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;

namespace MtgViewer.Pages.Decks;

public class StatisticsModel : PageModel
{
    private readonly CardDbContext _dbContext;

    public StatisticsModel(CardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public DeckPreview Deck { get; private set; } = default!;

    public Statistics Statistics { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancel)
    {
        var deck = await GetDeckAsync(id, cancel);

        if (deck is null)
        {
            return RedirectToPage("/Decks");
        }

        Deck = deck;

        Statistics = await GetStatisticsAsync(id, cancel);

        return Page();
    }

    private async Task<DeckPreview?> GetDeckAsync(int deckId, CancellationToken cancel)
    {
        return await _dbContext.Decks
            .Where(d => d.Id == deckId)
            .Select(d => new DeckPreview
            {
                Id = d.Id,
                Name = d.Name
            })
            .SingleOrDefaultAsync(cancel);
    }

    private async Task<Statistics> GetStatisticsAsync(int deckId, CancellationToken cancel)
    {
        var rarities = await GetRaritiesAsync(deckId, cancel);

        if (!rarities.Any())
        {
            return Statistics.CreateEmpty();
        }

        return new Statistics
        {
            Rarities = rarities,
            Colors = await GetColorsAsync(deckId, cancel),
            Types = await GetTypesAsync(deckId, cancel),
            ManaValues = await GetManaValuesAsync(deckId, cancel)
        };
    }

    private async Task<IReadOnlyDictionary<Rarity, int>> GetRaritiesAsync(int deckId, CancellationToken cancel)
    {
        return await _dbContext.Cards
            .Where(c => c.Holds.Any(h => h.LocationId == deckId)
                || c.Wants.Any(w => w.LocationId == deckId)
                || c.Givebacks.Any(g => g.LocationId == deckId))

            .GroupBy(
                c => c.Rarity,
                (Rarity, cs) => new
                {
                    Rarity,

                    HeldCopies = cs
                        .SelectMany(c => c.Holds)
                        .Where(h => h.LocationId == deckId)
                        .Sum(h => h.Copies),

                    WantCopies = cs
                        .SelectMany(c => c.Wants)
                        .Where(w => w.LocationId == deckId)
                        .Sum(w => w.Copies),

                    ReturnCopies = cs
                        .SelectMany(c => c.Givebacks)
                        .Where(g => g.LocationId == deckId)
                        .Sum(g => g.Copies)
                })

            .ToDictionaryAsync(
                rc => rc.Rarity,
                rc => rc.HeldCopies + rc.WantCopies - rc.ReturnCopies, cancel);
    }

    private async Task<IReadOnlyDictionary<Color, int>> GetColorsAsync(int deckId, CancellationToken cancel)
    {
        var dbColors = await _dbContext.Cards
            .Where(c => c.Holds.Any(h => h.LocationId == deckId)
                || c.Wants.Any(w => w.LocationId == deckId)
                || c.Givebacks.Any(g => g.LocationId == deckId))

            .GroupBy(
                c => c.Color,
                (Color, cs) => new
                {
                    Color,

                    HeldCopies = cs
                        .SelectMany(c => c.Holds)
                        .Where(h => h.LocationId == deckId)
                        .Sum(h => h.Copies),

                    WantCopies = cs
                        .SelectMany(c => c.Wants)
                        .Where(w => w.LocationId == deckId)
                        .Sum(w => w.Copies),

                    ReturnCopies = cs
                        .SelectMany(c => c.Givebacks)
                        .Where(g => g.LocationId == deckId)
                        .Sum(g => g.Copies)
                })

            .Select(cc =>
                new ColorCopies(cc.Color, cc.HeldCopies + cc.WantCopies - cc.ReturnCopies))

            .ToListAsync(cancel);

        ColorCopies GetColorCopies(Color color)
        {
            int copies = color is Color.None
                ? dbColors
                    .Where(cc => cc.Color is Color.None)
                    .Sum(cc => cc.Copies)

                : dbColors
                    .Where(cc => cc.Color.HasFlag(color))
                    .Sum(cc => cc.Copies);

            return new ColorCopies(color, copies);
        }

        return Enum
            .GetValues<Color>()
            .Select(GetColorCopies)
            .ToDictionary(cc => cc.Color, cc => cc.Copies);
    }

    private async Task<IReadOnlyDictionary<string, int>> GetTypesAsync(int deckId, CancellationToken cancel)
    {
        const StringComparison ordinal = StringComparison.Ordinal;

        var dbTypes = await _dbContext.Cards

            .Where(c => c.Holds.Any(h => h.LocationId == deckId)
                || c.Wants.Any(w => w.LocationId == deckId)
                || c.Givebacks.Any(g => g.LocationId == deckId))

            .Where(CardStatistics.TypeFilter)

            .Include(c => c.Holds
                .Where(h => h.LocationId == deckId))

            .Include(c => c.Wants
                .Where(w => w.LocationId == deckId))

            .Include(c => c.Givebacks
                .Where(g => g.LocationId == deckId))

            .AsSplitQuery()
            .AsAsyncEnumerable()

            .GroupByAwaitWithCancellation(
                (c, _) => c.Type.IndexOf(Symbol.LongDash) is > 0 and int i
                    ? ValueTask.FromResult(c.Type[..i])
                    : ValueTask.FromResult(c.Type),

                async (Type, cs, cnl) => new
                {
                    Type,

                    HeldCopies = await cs
                        .SelectMany(c => c.Holds
                            .ToAsyncEnumerable())
                        .SumAsync(h => h.Copies, cnl),

                    WantCopies = await cs
                        .SelectMany(c => c.Wants
                            .ToAsyncEnumerable())
                        .SumAsync(w => w.Copies, cnl),

                    ReturnCopies = await cs
                        .SelectMany(c => c.Givebacks
                            .ToAsyncEnumerable())
                        .SumAsync(g => g.Copies, cnl)
                })
            .ToListAsync(cancel);

        return CardStatistics.Types
            .Select(Type => new
            {
                Type,
                Copies = dbTypes
                    .Where(db => db.Type.Contains(Type, ordinal))
                    .Sum(db => db.HeldCopies + db.WantCopies - db.ReturnCopies)
            })
            .ToDictionary(tc => tc.Type, tc => tc.Copies);
    }

    private async Task<IReadOnlyDictionary<int, int>> GetManaValuesAsync(int deckId, CancellationToken cancel)
    {
        return await _dbContext.Cards

            .Where(c => c.Holds.Any(h => h.LocationId == deckId)
                || c.Wants.Any(w => w.LocationId == deckId)
                || c.Givebacks.Any(g => g.LocationId == deckId))

            .Include(c => c.Holds
                .Where(h => h.LocationId == deckId))

            .Include(c => c.Wants
                .Where(w => w.LocationId == deckId))

            .Include(c => c.Givebacks
                .Where(g => g.LocationId == deckId))

            .AsSplitQuery()
            .AsAsyncEnumerable()

            .GroupByAwaitWithCancellation(
                (c, _) => ValueTask.FromResult((int)c.ManaValue.GetValueOrDefault()),
                async (ManaValue, cs, cnl) => new
                {
                    ManaValue,

                    HeldCopies = await cs
                        .SelectMany(c => c.Holds
                            .ToAsyncEnumerable())
                        .SumAsync(h => h.Copies, cnl),

                    WantCopies = await cs
                        .SelectMany(c => c.Wants
                            .ToAsyncEnumerable())
                        .SumAsync(w => w.Copies, cnl),

                    ReturnCopies = await cs
                        .SelectMany(c => c.Givebacks
                            .ToAsyncEnumerable())
                        .SumAsync(g => g.Copies, cnl),
                })

            .ToDictionaryAsync(
                mc => mc.ManaValue,
                mc => mc.HeldCopies + mc.WantCopies - mc.ReturnCopies, cancel);
    }
}

