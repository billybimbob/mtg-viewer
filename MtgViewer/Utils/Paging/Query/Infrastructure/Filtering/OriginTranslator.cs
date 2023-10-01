using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal sealed class OriginTranslator
{
    private readonly ConstantExpression _origin;
    private readonly Dictionary<MemberExpression, MemberExpression> _translations;
    private readonly Dictionary<MemberExpression, bool> _nulls;

    public Type Type => _origin.Type;

    public OriginTranslator(ConstantExpression origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        var expressionEquality = ExpressionEqualityComparer.Instance;

        _origin = origin;

        _translations = new Dictionary<MemberExpression, MemberExpression>(expressionEquality);

        _nulls = new Dictionary<MemberExpression, bool>(expressionEquality);
    }

    public MemberExpression? Translate(MemberExpression member)
    {
        if (IsNull(member))
        {
            return null;
        }

        if (!_translations.TryGetValue(member, out var translation))
        {
            return null;
        }

        return translation;
    }

    private bool IsNull(MemberExpression member)
    {
        if (_nulls.TryGetValue(member, out bool isNull))
        {
            return isNull;
        }

        if (!_translations.TryGetValue(member, out var translation))
        {
            return true;
        }

        using var e = ExpressionHelpers
            .GetLineage(translation)
            .Reverse()
            .GetEnumerator();

        object? reference = _origin.Value;

        while (reference is not null
            && e.MoveNext()
            && e.Current.Member is PropertyInfo originProperty)
        {
            reference = originProperty.GetValue(reference);
        }

        return _nulls[member] = reference is null;
    }

    public bool IsParentNull(MemberExpression member)
    {
        if (member.Expression is not MemberExpression chain)
        {
            return false;
        }

        if (_nulls.TryGetValue(chain, out bool isNull))
        {
            return isNull;
        }

        if (!_translations.TryGetValue(member, out var translation))
        {
            return true;
        }

        using var e = ExpressionHelpers
            .GetLineage(translation)
            .Skip(1)
            .Reverse()
            .GetEnumerator();

        object? reference = _origin.Value;

        while (reference is not null
            && e.MoveNext()
            && e.Current.Member is PropertyInfo originProperty)
        {
            reference = originProperty.GetValue(reference);
        }

        return _nulls[chain] = reference is null;
    }

    public bool TryRegister(MemberExpression member)
    {
        if (_translations.ContainsKey(member))
        {
            return true;
        }

        return TryAddOriginProperty(member);
    }

    private bool TryAddOriginProperty(MemberExpression member)
    {
        using var e = ExpressionHelpers.GetLineage(member)
            .Reverse()
            .GetEnumerator();

        if (!e.MoveNext())
        {
            return false;
        }

        if (_origin.Type != e.Current.Expression?.Type)
        {
            return false;
        }

        var translation = Expression
            .MakeMemberAccess(_origin, e.Current.Member);

        while (e.MoveNext())
        {
            var property = translation.Type
                .GetTypeInfo()
                .GetProperty(e.Current.Member.Name);

            if (property is null)
            {
                return false;
            }

            translation = Expression
                .MakeMemberAccess(translation, property);
        }

        _translations.Add(member, translation);

        return true;
    }
}
