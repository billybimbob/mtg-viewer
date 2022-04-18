using System;
using System.Collections.Generic;

namespace MTGViewer.Data;

public class CardNameComparer : Comparer<Card>
{
    private static CardNameComparer? _instance;
    public static CardNameComparer Instance => _instance ??= new();

    public override int Compare(Card? x, Card? y)
    {
        const StringComparison currentCompare = StringComparison.CurrentCulture;

        int nameCompare = string.Compare(x?.Name, y?.Name, currentCompare);
        if (nameCompare != 0)
        {
            return nameCompare;
        }

        int setCompare = string.Compare(x?.SetName, y?.SetName, currentCompare);
        if (setCompare != 0)
        {
            return setCompare;
        }

        return string.Compare(x?.Id, y?.Id, currentCompare);
    }
}
