using System.Collections.Generic;
using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Filtering;

internal sealed class OriginTranslator
{
    private readonly ConstantExpression _origin;
    private readonly IReadOnlyDictionary<MemberExpression, MemberExpression> _translations;
    private readonly IReadOnlyDictionary<MemberExpression, bool> _nulls;

    public OriginTranslator(
        ConstantExpression origin,
        IReadOnlyDictionary<MemberExpression, MemberExpression> translations,
        IReadOnlyDictionary<MemberExpression, bool> computedNulls)
    {
        _origin = origin;
        _translations = translations;
        _nulls = computedNulls;
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
        if (_nulls.TryGetValue(member, out bool isNull) && isNull is false)
        {
            return false;
        }

        if (_origin.Value is null)
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
}
