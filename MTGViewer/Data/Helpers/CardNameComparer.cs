using System;
using System.Collections.Generic;

namespace MTGViewer.Data;

public class CardNameComparer : Comparer<Card>
{
    private static CardNameComparer? s_instance;
    public static CardNameComparer Instance => s_instance ??= new();

    public override int Compare(Card? cardA, Card? cardB)
    {
        const StringComparison currentCompare = StringComparison.CurrentCulture;

        int nameCompare = string.Compare(cardA?.Name, cardB?.Name, currentCompare);
        if (nameCompare != 0)
        {
            return nameCompare;
        }

        int setCompare = string.Compare(cardA?.SetName, cardB?.SetName, currentCompare);
        if (setCompare != 0)
        {
            return setCompare;
        }

        return string.Compare(cardA?.Id, cardB?.Id, currentCompare);
    }
}
