using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Data.Infrastructure;
using MTGViewer.Data.Projections;

namespace MTGViewer.Pages.Cards;

public class StatisticsModel : PageModel
{
    private readonly CardDbContext _dbContext;

    public StatisticsModel(CardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public LocationPreview? Location { get; private set; }

    public Statistics Statistics { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken cancel)
    {
        Location = await GetLocationAsync(id, cancel);

        if (Location is null && id is not null)
        {
            return RedirectToPage(new { id = null as int? });
        }

        Statistics = await GetStatisticsAsync(id, cancel);

        return Page();
    }

    private async Task<LocationPreview?> GetLocationAsync(int? locationId, CancellationToken cancel)
    {
        if (locationId is not int id)
        {
            return null;
        }

        return await _dbContext.Locations
            .Where(l => (l is Box || l is Deck) && (l.Id == id))
            .Select(l => new LocationPreview
            {
                Id = l.Id,
                Name = l.Name,
                Type = l.Type
            })
            .SingleOrDefaultAsync(cancel);
    }

    private async Task<Statistics> GetStatisticsAsync(int? locationId, CancellationToken cancel)
    {
        var rarities = await GetRaritiesAsync(locationId, cancel);

        if (!rarities.Any())
        {
            return Statistics.CreateEmpty();
        }

        return new Statistics
        {
            Rarities = rarities,
            Colors = await GetColorsAsync(locationId, cancel),
            Types = await GetTypesAsync(locationId, cancel),
            ManaValues = await GetManaValuesAsync(locationId, cancel)
        };
    }

    private async Task<IReadOnlyDictionary<Rarity, int>> GetRaritiesAsync(int? locationId, CancellationToken cancel)
    {
        return await _dbContext.Holds
            .Where(h => locationId == null || h.LocationId == locationId)
            .GroupBy(
                h => h.Card.Rarity,
                (Rarity, hs) => new { Rarity, Copies = hs.Sum(h => h.Copies) })
            .ToDictionaryAsync(
                rc => rc.Rarity,
                rc => rc.Copies, cancel);
    }

    private async Task<IReadOnlyDictionary<Color, int>> GetColorsAsync(int? locationId, CancellationToken cancel)
    {
        // technically bounded, with limit as the total combos of colors bits
        // which given 5 bits = 2^5 - 1 = 63 max size

        var dbColors = await _dbContext.Holds
            .Where(h => locationId == null || h.LocationId == locationId)
            .GroupBy(
                h => h.Card.Color,
                (color, hs) =>
                    new ColorCopies(color, hs.Sum(h => h.Copies)))
            .ToListAsync(cancel);

        return Enum
            .GetValues<Color>()
            .Select(GetColorCopies)
            .ToDictionary(cc => cc.Color, cc => cc.Copies);

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
    }

    private async Task<IReadOnlyDictionary<string, int>> GetTypesAsync(int? locationId, CancellationToken cancel)
    {
        const StringComparison ordinal = StringComparison.Ordinal;

        // all values in the type filter will appear before the long dash character

        var dbTypes = await _dbContext.Cards
            .Where(CardStatistics.TypeFilter)
            .GroupBy(
                c => c.Type.Contains(Symbol.LongDash)
                    ? c.Type.Substring(0, c.Type.IndexOf(Symbol.LongDash))
                    : c.Type,
                (Type, cs) => new
                {
                    Type,
                    Copies = cs
                        .SelectMany(c => c.Holds)
                        .Where(h => locationId == null || h.LocationId == locationId)
                        .Sum(h => h.Copies)
                })
            .ToListAsync(cancel); // keep eye on grouping size

        return CardStatistics.Types
            .Select(Type => new
            {
                Type,
                Copies = dbTypes
                    .Where(db => db.Type.Contains(Type, ordinal))
                    .Sum(tc => tc.Copies)
            })
            .ToDictionary(tc => tc.Type, tc => tc.Copies);
    }

    private async Task<IReadOnlyDictionary<int, int>> GetManaValuesAsync(int? locationId, CancellationToken cancel)
    {
        // mana value of null is treated as 0

        return await _dbContext.Holds
            .Where(h => locationId == null || h.LocationId == locationId)
            .GroupBy(
                h => (int)h.Card.ManaValue.GetValueOrDefault(),
                (ManaValue, hs) => new
                {
                    ManaValue,
                    Copies = hs.Sum(h => h.Copies)
                })
            .ToDictionaryAsync(
                mc => mc.ManaValue,
                mc => mc.Copies, cancel);
    }
}
