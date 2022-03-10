using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

public sealed class OriginTranslator<TOrigin, TEntity>
{
    private readonly Dictionary<MemberExpression, Expression> _translations;
    private readonly Dictionary<MemberExpression, bool> _nulls;

    private readonly Dictionary<MemberExpression, bool> _isNullable;
    private readonly NullabilityInfoContext _nullability;

    private readonly ConstantExpression _origin;
    private readonly MemberExpression? _selector;
    private readonly MemberInitExpression? _projection;


    public OriginTranslator(TOrigin origin, Expression<Func<TEntity, TOrigin>>? selector)
    {
        _translations = new(ExpressionEqualityComparer.Instance);
        _nulls = new(ExpressionEqualityComparer.Instance);

        _isNullable = new(ExpressionEqualityComparer.Instance);
        _nullability = new();

        var visitSelect = SelectorVisitor.Instance.Visit(selector);

        _origin = Expression.Constant(origin);
        _selector = visitSelect as MemberExpression;
        _projection = visitSelect as MemberInitExpression;
    }


    public Expression Translate(MemberExpression member)
    {
        if (IsChainNull(member))
        {
            return ExpressionConstants.Null;
        }

        if (!_translations.TryGetValue(member, out var translation))
        {
            throw new ArgumentException(nameof(member));
        }

        return translation;
    }


    public bool IsNonNull(MemberExpression member)
    {
        if (_isNullable.TryGetValue(member, out bool nullable))
        {
            return nullable;
        }

        if (member.Member is not PropertyInfo property)
        {
            return false;
        }

        var nullInfo = _nullability.Create(property);

        return _isNullable[member] = nullInfo.ReadState
            is NullabilityState.NotNull;
    }


    public bool IsChainNull(MemberExpression member)
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
            .GetLineage(translation as MemberExpression)
            .Reverse()
            .GetEnumerator();

        var reference = _origin.Value;

        while (reference is not null
            && e.MoveNext()
            && e.Current.Member is PropertyInfo originProperty)
        {
            reference = originProperty.GetValue(reference);
        }

        return _nulls[member] = reference is null;
    }


    public bool TryRegister(MemberExpression member)
    {
        if (_translations.ContainsKey(member))
        {
            return true;
        }

        if (TryAddChain(member))
        {
            return true;
        }
        
        if (TryAddFromProjection(member))
        {
            return true;
        }

        if (TryAddFlat(member))
        {
            return true;
        }

        return false;
    }


    private bool TryAddFromProjection(MemberExpression member)
    {
        if (_projection is null)
        {
            return false;
        }

        var registration = new Registration(member);

        if (registration.Count == 0)
        {
            return false;
        }

        return TryAddFromMemberInit(registration, 0, _origin, _projection);
    }


    private record Registration
    {
        public MemberExpression Expression { get; }
        public int Count { get; }
        public string LineageName { get; }

        public Registration(MemberExpression member)
        {
            Expression = member;

            Count = ExpressionHelpers
                .GetLineage(member)
                .Count();

            LineageName = ExpressionHelpers
                .GetLineageName(member);
        }
    }


    private bool TryAddFromMemberInit(
        Registration registration,
        int current,
        Expression caller,
        MemberInitExpression memberInit)
    {
        if (registration.Count == current)
        {
            _translations.Add(registration.Expression, caller);
            return true;
        }

        foreach (var binding in memberInit.Bindings)
        {
            if (binding is MemberAssignment assignment
                && TryAddFromAssignment(registration, current, caller, assignment))
            {
                return true;
            }
        }

        return false;
    }


    private bool TryAddFromAssignment(
        Registration registration,
        int current,
        Expression caller,
        MemberAssignment assignment)
    {
        var access = Expression.MakeMemberAccess(caller, assignment.Member);
        current++;

        return assignment.Expression switch
        {
            MemberExpression => TryAddFromMember(registration, current, access),
            MemberInitExpression i => TryAddFromMemberInit(registration, current, access, i), 
            ConditionalExpression c => TryAddFromConditional(registration, current, access, c),
            _ => false
        };
    }


    private bool TryAddFromMember(
        Registration registration,
        int current,
        MemberExpression caller)
    {
        const StringComparison ordinal = StringComparison.Ordinal;

        string callerLineage = ExpressionHelpers.GetLineageName(caller);

        if (callerLineage.Equals(registration.LineageName, ordinal))
        {
            _translations.Add(registration.Expression, caller);

            return true;
        }

        return false;
    }


    private bool TryAddFromConditional(
        Registration registration,
        int current,
        MemberExpression caller,
        ConditionalExpression condition)
    {
        if (condition.IfTrue is MemberInitExpression trueInit)
        {
            return TryAddFromMemberInit(registration, current, caller, trueInit);
        }

        if (condition.IfFalse is MemberInitExpression falseInit)
        {
            return TryAddFromMemberInit(registration, current, caller, falseInit);
        }

        if (condition.IfTrue is MemberExpression
            || condition.IfFalse is MemberExpression)
        {
            return TryAddFromMember(registration, current, caller);
        }

        return false;
    }



    private bool TryAddChain(MemberExpression member)
    {
        using var e = GetPropertyChain(member).GetEnumerator();

        if (!e.MoveNext()
            || e.Current.DeclaringType is null
            || !e.Current.DeclaringType.IsInstanceOfType(_origin.Value))
        {
            return false;
        }

        var originChain = Expression.Property(_origin, e.Current);

        while (e.MoveNext())
        {
            originChain = Expression.Property(originChain, e.Current);
        }

        _translations.Add(member, originChain);
        return true;
    }


    private bool TryAddFlat(MemberExpression member)
    {
        var lineageName = string.Join(
            string.Empty, GetPropertyChain(member).Select(p => p.Name));

        if (typeof(TOrigin).GetProperty(lineageName, member.Type) is PropertyInfo property)
        {
            _translations.Add(member, Expression.Property(_origin, property));
            return true;
        }

        return false;
    }


    private IEnumerable<PropertyInfo> GetPropertyChain(MemberExpression member)
    {
        using var e = ExpressionHelpers
            .GetLineage(_selector)
            .Reverse()
            .GetEnumerator();

        e.MoveNext();

        var memberLineage = ExpressionHelpers
            .GetLineage(member)
            .Reverse();

        foreach (var m in memberLineage)
        {
            if (m.Member is not PropertyInfo p)
            {
                continue;
            }

            if (m.Member == e.Current?.Member)
            {
                e.MoveNext();
                continue;
            }

            yield return p;
        }
    }


    private class SelectorVisitor : ExpressionVisitor
    {
        private static SelectorVisitor? _instance;
        public static ExpressionVisitor Instance => _instance ??= new();

        protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
        {
            if (node.Parameters.Count == 1
                && node.Parameters[0].Type == typeof(TEntity)
                && node.Body.Type == typeof(TOrigin))
            {
                return node.Body;
            }

            return ExpressionConstants.Null;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType is ExpressionType.Quote)
            {
                return node.Operand;
            }

            return node;
        }
    }


    private class TernaryVisitor : ExpressionVisitor
    {
        private static TernaryVisitor? _instance;
        public static ExpressionVisitor Instance => _instance ??= new();

        protected override Expression VisitConditional(ConditionalExpression node)
        {
            if (node.IfTrue is MemberInitExpression)
            {
                return node.IfTrue;
            }

            if (node.IfFalse is MemberInitExpression)
            {
                return node.IfFalse;
            }

            return node;
        }
    }
}