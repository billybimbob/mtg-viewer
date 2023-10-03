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
    private readonly IReadOnlyDictionary<MemberExpression, MemberExpression> _translations;
    private readonly IReadOnlyDictionary<MemberExpression, bool> _nulls;

    public Type Type => _origin.Type;

    private OriginTranslator(
        ConstantExpression origin,
        IReadOnlyDictionary<MemberExpression, MemberExpression> translations,
        IReadOnlyDictionary<MemberExpression, bool> computedNulls)
    {
        _origin = origin;
        _translations = translations;
        _nulls = computedNulls;
    }

    public static OriginTranslator Build(ConstantExpression origin, IReadOnlyList<MemberExpression> orderings)
    {
        ArgumentNullException.ThrowIfNull(origin);

        var translationBuilder = new OriginTranslationBuilder(origin, orderings);
        var translations = translationBuilder.Build();

        var nullBuilder = new OriginNullBuilder(origin, translations, orderings);
        var computedNulls = nullBuilder.Build();

        return new OriginTranslator(origin, translations, computedNulls);
    }

    public MemberExpression? Translate(MemberExpression member)
    {
        if (_nulls.GetValueOrDefault(member))
        {
            return null;
        }

        if (!_translations.TryGetValue(member, out var translation))
        {
            return null;
        }

        return translation;
    }

    public bool IsMemberNull(MemberExpression member)
    {
        if (_nulls.TryGetValue(member, out bool isNull) && !isNull)
        {
            return false;
        }

        if (member.Expression is MemberExpression chain
            && _nulls.GetValueOrDefault(chain))
        {
            return false;
        }

        return true;
    }

    private sealed class OriginTranslationBuilder
    {
        private readonly ConstantExpression _origin;
        private readonly IEnumerable<MemberExpression> _targets;

        public OriginTranslationBuilder(ConstantExpression origin, IEnumerable<MemberExpression> targets)
        {
            _origin = origin;
            _targets = targets;
        }

        public IReadOnlyDictionary<MemberExpression, MemberExpression> Build()
        {
            var translations = new Dictionary<MemberExpression, MemberExpression>(ExpressionEqualityComparer.Instance);

            foreach (var target in _targets)
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
    }

    private sealed class OriginNullBuilder
    {
        private readonly ConstantExpression _origin;
        private readonly IReadOnlyDictionary<MemberExpression, MemberExpression> _translations;
        private readonly IEnumerable<MemberExpression> _targets;

        public OriginNullBuilder(
            ConstantExpression origin,
            IReadOnlyDictionary<MemberExpression, MemberExpression> translations,
            IEnumerable<MemberExpression> targets)
        {
            _origin = origin;
            _translations = translations;
            _targets = targets;
        }

        public IReadOnlyDictionary<MemberExpression, bool> Build()
        {
            var computedNulls = new Dictionary<MemberExpression, bool>(ExpressionEqualityComparer.Instance);

            foreach (var target in _targets)
            {
                if (computedNulls.ContainsKey(target))
                {
                    continue;
                }

                if (!_translations.TryGetValue(target, out var translation))
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
}
