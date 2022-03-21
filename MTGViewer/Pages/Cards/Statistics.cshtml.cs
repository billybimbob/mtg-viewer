using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;

namespace MTGViewer.Pages.Cards;

public class StatisticsModel : PageModel
{
    private readonly CardDbContext _dbContext;

    public StatisticsModel(CardDbContext dbContext)
    {
        _dbContext = dbContext;
    }


    public Statistics Statistics { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancel)
    {
        var rarities = await _dbContext.Holds
            .GroupBy(
                h => h.Card.Rarity,
                (Rarity, hs) => new { Rarity, Total = hs.Sum(h => h.Copies) })
            .ToDictionaryAsync(
                rt => rt.Rarity,
                rt => rt.Total, cancel);

        if (!rarities.Any())
        {
            return;
        }

        // cards with a null mana value are treated as having a mana value of 0

        Statistics = new Statistics
        {
            RarityTotal = rarities,

            ColorTotal = await GetColorTotalsAsync(cancel),

            TypeTotal = await GetTypeTotalsAsync(cancel),

            ManaValueTotal = await _dbContext.Holds
                .GroupBy(
                    h => (int)h.Card.ManaValue.GetValueOrDefault(),
                    (ManaValue, hs) => new { ManaValue, Total = hs.Sum(h => h.Copies) })
                .ToDictionaryAsync(
                    mt => mt.ManaValue,
                    mt => mt.Total, cancel)
        };
    }



    private readonly record struct ColorTotal(Color Color, int Total);


    private async Task<IReadOnlyDictionary<Color, int>> GetColorTotalsAsync(CancellationToken cancel)
    {
        var dbColors = await _dbContext.Holds
            .GroupBy(h => h.Card.Color,
                (color, hs) => 
                    new ColorTotal(color, hs.Sum(h => h.Copies)))
            .ToListAsync(cancel);

        return Enum
            .GetValues<Color>()
            .Select(c => GetColorTotal(dbColors, c))
            .ToDictionary(ct => ct.Color, ct => ct.Total);
    }


    private ColorTotal GetColorTotal(IReadOnlyList<ColorTotal> dbColors, Color color)
    {
        int total = color is Color.None
            ? dbColors
                .Where(ct => ct.Color is Color.None)
                .Sum(ct => ct.Total)
            : dbColors
                .Where(ct => (ct.Color & color) == color)
                .Sum(ct => ct.Total);

        return new ColorTotal(color, total);
    }



    public static readonly string[] CardTypes = new[]
    {
        "Artifact", "Creature", "Enchantment", "Instant", "Land", "Sorcery"
    };



    private readonly record struct TypeTotal(string Type, int Total);


    private async Task<IReadOnlyDictionary<string, int>> GetTypeTotalsAsync(CancellationToken cancel)
    {
        const StringComparison ordinal = StringComparison.Ordinal;

        var dbTypes = await _dbContext.Holds
            .Where(h => h.Card.Type.Contains("Artifact")
                || h.Card.Type.Contains("Creature")
                || h.Card.Type.Contains("Enchantment")
                || h.Card.Type.Contains("Instant")
                || h.Card.Type.Contains("Land")
                || h.Card.Type.Contains("Sorcery"))

            .Select(h => new TypeTotal(h.Card.Type, h.Copies))
            .ToListAsync(cancel);

        return CardTypes
            .Select(type => new TypeTotal
            {
                Type = type,
                Total = dbTypes
                    .Where(db => db.Type.Contains(type, ordinal))
                    .Sum(tt => tt.Total)
            })
            .ToDictionary(tt => tt.Type, tt => tt.Total);
    }
}