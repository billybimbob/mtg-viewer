using System;
using System.Collections.Generic;

namespace MTGViewer.Data;

public class CardNameComparer : Comparer<Card>
{
    private const StringComparison CurrentCompare = StringComparison.CurrentCulture;

    private static CardNameComparer? _instance;
    public static CardNameComparer Instance => _instance ??= new();

    public override int Compare(Card? cardA, Card? cardB)
    {
        int nameCompare = string.Compare(cardA?.Name, cardB?.Name, CurrentCompare);
        if (nameCompare != 0)
        {
            return nameCompare;
        }

        int setCompare = string.Compare(cardA?.SetName, cardB?.SetName, CurrentCompare);
        if (setCompare != 0)
        {
            return setCompare;
        }

        return string.Compare(cardA?.Id, cardB?.Id, CurrentCompare);
    }
}
