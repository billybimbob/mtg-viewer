using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Projections;

public sealed class Statistics
{
    public IReadOnlyDictionary<Rarity, int> Rarities { get; init; } = default!;

    public IReadOnlyDictionary<Color, int> Colors { get; init; } = default!;

    public IReadOnlyDictionary<string, int> Types { get; init; } = default!;

    public IReadOnlyDictionary<int, int> ManaValues { get; init; } = default!;

    public int Copies => Rarities.Values.Sum();

    public float ManaValueAvg
    {
        get
        {
            float manaTotal = ManaValues.Values.Sum();

            return manaTotal is 0f ? 0f : ManaValues.Sum(kv => kv.Key * kv.Value) / manaTotal;
        }
    }

    public static Statistics CreateEmpty()
    {
        return new Statistics
        {
            Rarities = new Dictionary<Rarity, int>(),
            Colors = new Dictionary<Color, int>(),
            Types = new Dictionary<string, int>(),
            ManaValues = new Dictionary<int, int>()
        };
    }
}
