using System.Collections.Generic;
using System.Linq;

namespace MtgViewer.Data.Projections;

public sealed class Statistics
{
    public static Statistics Empty { get; }
        = new()
        {
            Rarities = new Dictionary<Rarity, int>(),
            Colors = new Dictionary<Color, int>(),
            Types = new Dictionary<string, int>(),
            ManaValues = new Dictionary<int, int>()
        };

    public required IReadOnlyDictionary<Rarity, int> Rarities { get; init; }

    public required IReadOnlyDictionary<Color, int> Colors { get; init; }

    public required IReadOnlyDictionary<string, int> Types { get; init; }

    public required IReadOnlyDictionary<int, int> ManaValues { get; init; }

    public int Copies => Rarities.Values.Sum();

    public float ManaValueAvg
    {
        get
        {
            float manaTotal = ManaValues.Values.Sum();
            return manaTotal is 0f ? 0f : ManaValues.Sum(kv => kv.Key * kv.Value) / manaTotal;
        }
    }
}
