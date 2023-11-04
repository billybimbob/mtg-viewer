using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using EntityFrameworkCore.Paging.Query.Infrastructure;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query.Filtering;

internal sealed class OriginTranslatorBuilder
{
    private readonly ConstantExpression _origin;

    public OriginTranslatorBuilder(ConstantExpression origin)
    {
        _origin = origin;
    }

    public OriginTranslator Build(SeekOrderCollection orderCollection)
    {
        var targets = orderCollection.OrderProperties
            .Select(o => o.Member)
            .OfType<MemberExpression>()
            .ToList();

        var translations = BuildTranslations(targets);
        var computedNulls = BuildComputedNulls(targets, translations);

        return new OriginTranslator(_origin, translations, computedNulls);
    }

    public IReadOnlyDictionary<MemberExpression, MemberExpression> BuildTranslations(IReadOnlyList<MemberExpression> targets)
    {
        var translations = new Dictionary<MemberExpression, MemberExpression>(ExpressionEqualityComparer.Instance);

        foreach (var target in targets)
        {
            if (translations.ContainsKey(target))
            {
                continue;
            }

            var translation = FindOriginProperty(target);

            if (translation is not null)
            {
                translations.Add(target, translation);
            }
        }

        return translations;
    }

    private MemberExpression? FindOriginProperty(MemberExpression member)
    {
        using var e = ExpressionHelpers.GetLineage(member)
            .Reverse()
            .GetEnumerator();

        if (!e.MoveNext())
        {
            return null;
        }

        if (_origin.Type != e.Current.Expression?.Type)
        {
            return null;
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
                return null;
            }

            translation = Expression
                .MakeMemberAccess(translation, property);
        }

        return translation;
    }

    public IReadOnlyDictionary<MemberExpression, bool> BuildComputedNulls(
        IReadOnlyList<MemberExpression> targets,
        IReadOnlyDictionary<MemberExpression, MemberExpression> translations)
    {
        var computedNulls = new Dictionary<MemberExpression, bool>(ExpressionEqualityComparer.Instance);

        foreach (var target in targets)
        {
            if (computedNulls.ContainsKey(target))
            {
                continue;
            }

            if (!translations.TryGetValue(target, out var translation))
            {
                continue;
            }

            computedNulls.Add(target, ComputeNullValue(translation));
        }

        return computedNulls;
    }

    private bool ComputeNullValue(MemberExpression member)
    {
        using var e = ExpressionHelpers
            .GetLineage(member)
            .Reverse()
            .GetEnumerator();

        object? reference = _origin.Value;

        while (reference is not null
            && e.MoveNext()
            && e.Current.Member is PropertyInfo originProperty)
        {
            reference = originProperty.GetValue(reference);
        }

        return reference is null;
    }
}
