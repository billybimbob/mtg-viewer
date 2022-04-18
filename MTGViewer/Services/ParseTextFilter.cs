using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Services;

public readonly record struct TextFilter(string? Name, string[]? Types, string? Text)
{
    public const int TextLimit = 40;
    public const int TypeLimit = 8;

    public bool Equals(TextFilter other)
    {
        return Name == other.Name
            && Text == other.Text
            && TypesEqual(other.Types);
    }

    private bool TypesEqual(string[]? types)
    {
        return Types is null && types is null
            || Types is not null
                && types is not null
                && Types.SequenceEqual(types);
    }

    public override int GetHashCode()
    {
        return Name?.GetHashCode() ?? 0
            ^ Text?.GetHashCode() ?? 0
            ^ Types?.Aggregate(0,
                (hash, s) => hash ^ s.GetHashCode()) ?? 0;
    }

    public override string ToString()
    {
        return (Name ?? "")

            + (Types is { Length: > 0 }
                ? $"{ParseTextFilter.SearchType} {string.Join(' ', Types)}" : "")

            + (Text is not null
                ? $"{ParseTextFilter.SearchText} {Text}" : "");
    }
}

public class ParseTextFilter
{
    public const string SearchName = "/n";
    public const string SearchType = "/t";
    public const string SearchText = "/o";

    private const string Split = $@"(?<{nameof(Split)}>\/[nto])\s+";

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

        if (capture.SequenceEqual(SearchType) && filter.Types is not { Length: > 0 })
        {
            return filter with { Types = TextString(text).Split() };
        }

        if (capture.SequenceEqual(SearchText) && filter.Text is null)
        {
            return filter with { Text = TextString(text) };
        }

        if (filter.Name is null)
        {
            return filter with { Name = TextString(text) };
        }

        return filter;
    }

    private static string TextString(ReadOnlySpan<char> text)
    {
        return text.Trim().ToString();
    }
}
