using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Projections;

public sealed class Statistics
{
    public IReadOnlyDictionary<Rarity, int> Rarities { get; init; } = default!;
    public IReadOnlyDictionary<Color, int> Colors { get; init; } = default!;

    public IReadOnlyDictionary<string, int> Types { get; init; } = default!;
    public IReadOnlyDictionary<int, int> ManaValues { get; init; } = default!;

    // either rarity or mana value sum could be used
    public int Copies => Rarities.Values.Sum();

    public float ManaValueAvg =>
        ManaValues.Sum(kv => kv.Key * (float)kv.Value) / Copies;
}
