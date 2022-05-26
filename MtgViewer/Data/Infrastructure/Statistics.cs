using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MtgViewer.Data.Infrastructure;

internal readonly record struct ColorCopies(Color Color, int Copies);

internal static class CardStatistics
{
    public static readonly IReadOnlyList<string> Types
        = new string[]
        {
            "Artifact",
            "Creature",
            "Enchantment",
            "Instant",
            "Land",
            "Sorcery"
        };

    public static Expression<Func<Card, bool>> TypeFilter
        => card => card.Type.Contains("Artifact")
            || card.Type.Contains("Creature")
            || card.Type.Contains("Enchantment")
            || card.Type.Contains("Instant")
            || card.Type.Contains("Land")
            || card.Type.Contains("Sorcery");
}

