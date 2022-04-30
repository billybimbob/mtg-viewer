using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Services;

public readonly record struct TextFilter(string? Name, string? Types, string? Text)
{
    public const int Limit = 40;
}

public class ParseTextFilter
{
    public const string SearchName = "/n";
    public const string SearchType = "/t";
    public const string SearchText = "/o";

    private const string Split = $@"\s*(?<{nameof(Split)}>\/[nto])\s+";

    private readonly ILogger<ParseTextFilter> _logger;

    public ParseTextFilter(ILogger<ParseTextFilter> logger)
    {
        _logger = logger;
    }

    public TextFilter Parse(string? search)
    {
        if (string.IsNullOrEmpty(search))
        {
            return default;
        }

        var filter = new TextFilter();
        var match = Regex.Match(search, Split);

        var source = search.AsSpan();
        var capture = ReadOnlySpan<char>.Empty;

        int index = 0;

        while (match.Success)
        {
            _logger.LogInformation("Received match {Match}", match);

            filter = AddFilter(filter, capture, source[index..match.Index]);

            capture = match.Groups[nameof(Split)].ValueSpan;
            index = match.Index + match.Length;

            match = match.NextMatch();
        }

        return AddFilter(filter, capture, source[index..]);
    }

    private static TextFilter AddFilter(
        TextFilter filter,
        ReadOnlySpan<char> capture,
        ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || text.IsWhiteSpace())
        {
            return filter;
        }

        if (capture.SequenceEqual(SearchType) && filter.Types is null)
        {
            return filter with { Types = text.ToString() };
        }

        if (capture.SequenceEqual(SearchText) && filter.Text is null)
        {
            return filter with { Text = text.ToString() };
        }

        if (filter.Name is null)
        {
            return filter with { Name = text.ToString() };
        }

        return filter;
    }
}
