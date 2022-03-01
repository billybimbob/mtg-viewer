using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using MtgApiManager.Lib.Service;
using MTGViewer.Data;

namespace MTGViewer.Services;

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
            var types = values
                .Select(Validate)
                .OfType<string>()
                .ToArray();

            return new MtgTypeParameter(types);
        }
        else if (Validate(value?.ToString()) is string valid)
        {
            return new MtgTypeParameter(valid.Split());
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

    public void Apply(ICardService cards)
    {
        if (_types is null)
        {
            return;
        }

        var types = string.Join(MtgApiQuery.And, _types);

        cards.Where(q => q.Type, types);
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
        if (value is int pageSize and >0)
        {
            return new MtgPageSizeParameter(pageSize);
        }

        return this;
    }

    public void Apply(ICardService cards)
    {
        if (_pageSize > 0)
        {
            cards.Where(q => q.PageSize, _pageSize);
        }
    }
}


internal class MtgPageParameter : IMtgParameter
{
    public MtgPageParameter() : this(1)
    { }

    private MtgPageParameter(int page)
    {
        Page = page;
    }

    public int Page { get; }
    public bool IsEmpty => Page <= 1;

    public IMtgParameter Accept(object? value)
    {
        if (value is int page and >1)
        {
            return new MtgPageParameter(page);
        }

        return this;
    }

    public void Apply(ICardService cards)
    {
        if (Page > 1)
        {
            cards.Where(q => q.Page, Page);
        }
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

    public void Apply(ICardService cards)
    {
        if (_color is Color.None)
        {
            return;
        }

        var colorNames = Enum.GetValues<Color>()
            .Where(c => c is not Color.None && _color.HasFlag(c))
            .Select(c => c.ToString().ToLower());

        var colors = string.Join(MtgApiQuery.And, colorNames);

        cards.Where(q => q.Colors, colors);
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
        if (TryToString(value, out var parameter))
        {
            return new MtgDefaultParameter(_property, parameter);
        }

        return this;
    }

    private static bool TryToString(object? paramValue, out string stringValue)
    {
        var toString = paramValue?.ToString();

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

    public void Apply(ICardService cards)
    {
        if (_value is not null)
        {
            cards.Where(_property, _value);
        }
    }
}


public static class CardQueryParameters
{
    private static Dictionary<string, IMtgParameter>? _base;
    internal static IReadOnlyDictionary<string, IMtgParameter> Base =>
        _base ??= new()
        {
            [nameof(CardQuery.Colors)] = new MtgColorParameter(),
            [nameof(CardQuery.Type)] = new MtgTypeParameter(),
            [nameof(CardQuery.Page)] = new MtgPageParameter(),
            [nameof(CardQuery.PageSize)] = new MtgPageSizeParameter()
        };
}