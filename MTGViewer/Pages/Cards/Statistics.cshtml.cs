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
    public static readonly IReadOnlyList<string> CardTypes = new[]
    {
        "Artifact", "Creature", "Enchantment", "Instant", "Land", "Sorcery"
    };

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
            Rarities = rarities,

            Colors = await GetColorsAsync(cancel),

            Types = await GetTypesAsync(cancel),

            ManaValues = await _dbContext.Holds
                .GroupBy(
                    h => (int)h.Card.ManaValue.GetValueOrDefault(),
                    (ManaValue, hs) => new
                    {
                        ManaValue,
                        Copies = hs.Sum(h => h.Copies)
                    })
                .ToDictionaryAsync(
                    mc => mc.ManaValue,
                    mc => mc.Copies, cancel)
        };
    }

    private readonly record struct ColorCopies(Color Color, int Copies);

    private readonly record struct TypeCopies(string Type, int Copies);

    private async Task<IReadOnlyDictionary<Color, int>> GetColorsAsync(CancellationToken cancel)
    {
        // technically bounded, with limit as the total combos of colors bits
        // which given 5 bits = 2^5 - 1 = 63 max size

        var dbColors = await _dbContext.Holds
            .GroupBy(
                h => h.Card.Color,
                (color, hs) =>
                    new ColorCopies(color, hs.Sum(h => h.Copies)))
            .ToListAsync(cancel);

        return Enum
            .GetValues<Color>()
            .Select(c => GetColorCopies(dbColors, c))
            .ToDictionary(cc => cc.Color, cc => cc.Copies);
    }

    private static ColorCopies GetColorCopies(IReadOnlyList<ColorCopies> dbColors, Color color)
    {
        int copies = color is Color.None
            ? dbColors
                .Where(cc => cc.Color is Color.None)
                .Sum(cc => cc.Copies)
            : dbColors
                .Where(cc => (cc.Color & color) == color)
                .Sum(cc => cc.Copies);

        return new ColorCopies(color, copies);
    }

    private async Task<IReadOnlyDictionary<string, int>> GetTypesAsync(CancellationToken cancel)
    {
        const StringComparison ordinal = StringComparison.Ordinal;
        const string longDash = "\u2014";

        var dbTypes = await _dbContext.Cards
            .Where(c => c.Type.Contains("Artifact")
                || c.Type.Contains("Creature")
                || c.Type.Contains("Enchantment")
                || c.Type.Contains("Instant")
                || c.Type.Contains("Land")
                || c.Type.Contains("Sorcery"))

            .GroupBy(
                c => c.Type.Contains(longDash)
                    ? c.Type.Substring(0, c.Type.IndexOf(longDash))
                    : c.Type,
                (type, cs) => new TypeCopies
                {
                    Type = type,
                    Copies = cs
                        .SelectMany(c => c.Holds)
                        .Sum(h => h.Copies)
                })
            .ToListAsync(cancel); // keep eye on grouping size

        return CardTypes
            .Select(type => new TypeCopies
            {
                Type = type,
                Copies = dbTypes
                    .Where(db => db.Type.Contains(type, ordinal))
                    .Sum(tc => tc.Copies)
            })
            .ToDictionary(tc => tc.Type, tc => tc.Copies);
    }
}
