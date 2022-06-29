using System;

namespace EntityFrameworkCore.Paging;

public readonly record struct Offset(int Current, int Total)
{
    public int Current { get; init; } = Math.Max(Current, 0);
    public int Total { get; init; } = Math.Max(Total, 0);

    public bool HasPrevious => Current > 0;
    public bool HasNext => Current < Total - 1;
    public bool HasMultiple => Total > 1;

    public Offset(int currentPage, int totalItems, int pageSize)
        : this(currentPage, TotalPages(totalItems, pageSize))
    { }

    private static int TotalPages(int totalItems, int pageSize)
    {
        totalItems = Math.Max(totalItems, 0);
        pageSize = Math.Max(pageSize, 1);

        return (int)Math.Ceiling((double)totalItems / pageSize);
    }

    public override string ToString() => Current == Total
        ? $"{Current}/{Total}"
        : $"{Current + 1}/{Total}";
}
