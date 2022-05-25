using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using MtgApiManager.Lib.Service;

using MTGViewer.Data;

namespace MTGViewer.Services.Search;

internal class MtgTypeParameter : IMtgParameter
{
    public MtgTypeParameter() : this(Array.Empty<string>())
    { }

    private MtgTypeParameter(string[] types)
    {
        _types = types;
    }

    private readonly string[] _types;
    public bool IsEmpty => !_types.Any();

    public IMtgParameter Accept(object? value)
    {
        if (value is IEnumerable<string> values)
        {
            string[] types = values
                .Select(Validate)
                .OfType<string>()
                .Concat(_types)
                .ToArray();

            return new MtgTypeParameter(types);
        }
        else if (Validate(value?.ToString()) is string valid)
        {
            return new MtgTypeParameter(
                valid.Split().Concat(_types).ToArray());
        }

        return this;
    }

    private string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    public ICardService Apply(ICardService cards)
    {
        if (!_types.Any())
        {
            return cards;
        }

        string types = string.Join(MtgApiQuery.And, _types);

        return cards.Where(q => q.Type, types);
    }
}

internal class MtgPageSizeParameter : IMtgParameter
{
    public MtgPageSizeParameter() : this(0)
    { }

    private MtgPageSizeParameter(int pageSize)
    {
        _pageSize = pageSize;
    }

    private readonly int _pageSize;
    public bool IsEmpty => _pageSize <= 0;

    public IMtgParameter Accept(object? value)
    {
        if (value is int pageSize and > 0 and <= MtgApiQuery.Limit)
        {
            return new MtgPageSizeParameter(pageSize);
        }

        return this;
    }

    public ICardService Apply(ICardService cards)
    {
        if (_pageSize > 0)
        {
            return cards.Where(q => q.PageSize, _pageSize);
        }

        return cards;
    }
}

internal class MtgPageParameter : IMtgParameter
{
    public MtgPageParameter() : this(0)
    { }

    private MtgPageParameter(int page)
    {
        Page = page;
    }

    public int Page { get; }
    public bool IsEmpty => Page == 0;

    public IMtgParameter Accept(object? value)
    {
        if (value is int page and > 0)
        {
            return new MtgPageParameter(page);
        }

        return this;
    }

    public ICardService Apply(ICardService cards)
    {
        if (Page > 0)
        {
            // query starts at index 1 instead of 0
            return cards.Where(q => q.Page, Page + 1);
        }

        return cards;
    }
}

internal class MtgColorParameter : IMtgParameter
{
    public MtgColorParameter() : this(Color.None)
    { }

    private MtgColorParameter(Color color)
    {
        _color = color;
    }

    private readonly Color _color;
    public bool IsEmpty => _color is Color.None;

    public IMtgParameter Accept(object? value)
    {
        if (value is Color color and not Color.None)
        {
            return new MtgColorParameter(color);
        }

        return this;
    }

    public ICardService Apply(ICardService cards)
    {
        if (_color is Color.None)
        {
            return cards;
        }

        var colorNames = Symbol.Colors
            .Where(kv => _color.HasFlag(kv.Key))
            .Select(kv => kv.Value);

        string colors = string.Join(MtgApiQuery.And, colorNames);

        return cards.Where(q => q.ColorIdentity, colors);
    }
}

internal class MtgRarityParameter : IMtgParameter
{
    public MtgRarityParameter() : this(null)
    { }

    private MtgRarityParameter(Rarity? rarity)
    {
        _rarity = rarity;
    }

    private readonly Rarity? _rarity;
    public bool IsEmpty => _rarity is null;

    public IMtgParameter Accept(object? value)
    {
        if (value is Rarity rarity)
        {
            return new MtgRarityParameter(rarity);
        }

        return this;
    }

    public ICardService Apply(ICardService cards)
    {
        if (_rarity is not Rarity rarity)
        {
            return cards;
        }

        var rarities = Enum.GetValues<Rarity>();

        if (!rarities.Contains(rarity))
        {
            return cards;
        }

        return cards.Where(q => q.Rarity, rarity.ToString());
    }
}

internal class MtgDefaultParameter : IMtgParameter
{
    public MtgDefaultParameter(Expression<Func<CardQueryParameter, string>> property)
    {
        _property = property;
        _value = null;
    }

    private MtgDefaultParameter(Expression<Func<CardQueryParameter, string>> property, string value)
    {
        _property = property;
        _value = value;
    }

    private readonly Expression<Func<CardQueryParameter, string>> _property;
    private readonly string? _value;

    public bool IsEmpty => _value is null;

    public IMtgParameter Accept(object? value)
    {
        if (TryToString(value, out string parameter))
        {
            return new MtgDefaultParameter(_property, parameter);
        }

        return this;
    }

    private static bool TryToString(object? paramValue, out string stringValue)
    {
        string? toString = paramValue?.ToString();

        if (toString == null)
        {
            stringValue = null!;
            return false;
        }

        toString = toString.Split(MtgApiQuery.Or).FirstOrDefault();

        // make sure that only one specific card is searched for

        if (string.IsNullOrWhiteSpace(toString))
        {
            stringValue = null!;
            return false;
        }

        stringValue = toString.Trim();
        return true;
    }

    public ICardService Apply(ICardService cards)
    {
        if (_value is not null)
        {
            return cards.Where(_property, _value);
        }

        return cards;
    }
}

public static class CardQueryParameters
{
    private static Dictionary<string, IMtgParameter>? _base;
    internal static IReadOnlyDictionary<string, IMtgParameter> Base =>
        _base ??= new Dictionary<string, IMtgParameter>
        {
            [nameof(CardQuery.Colors)] = new MtgColorParameter(),
            [nameof(CardQuery.Rarity)] = new MtgRarityParameter(),
            [nameof(CardQuery.Type)] = new MtgTypeParameter(),
            [nameof(CardQuery.Page)] = new MtgPageParameter(),
            [nameof(CardQuery.PageSize)] = new MtgPageSizeParameter()
        };
}
