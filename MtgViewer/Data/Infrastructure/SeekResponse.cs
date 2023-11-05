using System;
using System.Linq;

using EntityFrameworkCore.Paging;

namespace MtgViewer.Data.Infrastructure;

public sealed class SeekResponse<T> where T : class
{
    public bool HasPrevious { get; set; }

    public bool HasNext { get; set; }

    public bool IsPartial { get; set; }

    public T[] Data { get; set; } = Array.Empty<T>();
}

public static class SeekExtensions
{
    public static SeekResponse<T> ToSeekResponse<T>(this SeekList<T> seekList) where T : class
    {
        return new SeekResponse<T>
        {
            HasPrevious = seekList.Seek.Previous is not null,
            HasNext = seekList.Seek.Next is not null,
            IsPartial = seekList.Seek.IsPartial,
            Data = seekList.ToArray()
        };
    }

    public static SeekList<T> ToSeekList<T>(this SeekResponse<T> seekResponse) where T : class
    {
        return new SeekList<T>(
            seekResponse.Data,
            seekResponse.HasPrevious,
            seekResponse.HasNext,
            seekResponse.IsPartial);
    }
}
