using System;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using MtgViewer.Data;

namespace MtgViewer.Services;

public readonly record struct ManaFilter(ExpressionType Comparison, float Value)
{
    public Expression<Func<Card, bool>> CreateFilter()
    {
        var cardParameter = Expression.Parameter(typeof(Card), nameof(Card).ToLowerInvariant()[0].ToString());

        var body = Expression.MakeBinary(
            Comparison,
            Expression.Property(cardParameter, nameof(Card.ManaValue)),
            Expression.Constant(Value, typeof(float?)));

        return Expression.Lambda<Func<Card, bool>>(body, cardParameter);
    }

    public Expression<Func<TQuantity, bool>> CreateFilter<TQuantity>() where TQuantity : Quantity
    {
        var quantityParameter = Expression.Parameter(typeof(TQuantity), typeof(TQuantity).Name.ToLowerInvariant()[0].ToString());

        var cardProperty = Expression.Property(quantityParameter, nameof(Quantity.Card));

        var body = Expression.MakeBinary(
            Comparison,
            Expression.Property(cardProperty, nameof(Card.ManaValue)),
            Expression.Constant(Value, typeof(float?)));

        return Expression.Lambda<Func<TQuantity, bool>>(body, quantityParameter);
    }
}

public readonly record struct TextFilter(string? Name, ManaFilter? Mana, string? Types, string? Text)
{
    public const int Limit = 40;
}

public class ParseTextFilter
{
    public const string SearchName = "/n";
    public const string SearchMana = "/m";
    public const string SearchType = "/t";
    public const string SearchText = "/o";

    private const string Split = $@"\s*(?<{nameof(Split)}>\/[nmto])\s+";
    private const string ManaSplit = @"(?<Comparison>[\>\<]?=?)\s*(?<Value>\d+)";

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

        var match = Regex.Match(search, Split);
        var source = search.AsSpan();

        var filter = new TextFilter();
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

        if (capture.SequenceEqual(SearchMana) && filter.Mana is null)
        {
            return filter with { Mana = ParseManaFilter(text.ToString()) };
        }

        if (filter.Name is null)
        {
            return filter with { Name = text.ToString() };
        }

        return filter;
    }

    private static ManaFilter? ParseManaFilter(string text)
    {
        var match = Regex.Match(text, ManaSplit);

        if (!float.TryParse(match.Groups["Value"].ValueSpan, out float value))
        {
            return null;
        }

        var comparison = match.Groups["Comparison"].Value switch
        {
            ">" => ExpressionType.GreaterThan,
            ">=" => ExpressionType.GreaterThanOrEqual,
            "<" => ExpressionType.LessThan,
            "<=" => ExpressionType.LessThanOrEqual,
            "=" or _ => ExpressionType.Equal
        };

        return new ManaFilter(comparison, value);
    }
}
