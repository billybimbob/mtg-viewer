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

    private readonly ConstantExpression _origin;

    private readonly MemberExpression? _selector;
    private readonly MemberInitExpression? _projection;


    public OriginTranslator(TOrigin origin, Expression<Func<TEntity, TOrigin>>? selector)
    {
        _translations = new(ExpressionEqualityComparer.Instance);
        _nulls = new(ExpressionEqualityComparer.Instance);

        _origin = Expression.Constant(origin);

        var visitSelect = SelectorVisitor.Instance.Visit(selector);

        _selector = visitSelect as MemberExpression;
        _projection = visitSelect as MemberInitExpression;
    }


    public Expression Translate(MemberExpression member)
    {
        if (_nulls.GetValueOrDefault(member))
        {
            return ExpressionConstants.Null;
        }

        if (!_translations.TryGetValue(member, out var translation))
        {
            throw new ArgumentException(nameof(member));
        }

        return translation;
    }


    public bool IsNull(MemberExpression member)
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

        var lineage = ExpressionHelpers
            .GetLineage(member)
            .Reverse()
            .ToList();

        if (!lineage.Any())
        {
            return false;
        }

        return TryAddFromMemberInit(lineage, 0, _origin, _projection);
    }


    private bool TryAddFromMemberInit(
        IReadOnlyList<MemberExpression> lineage,
        int current,
        Expression caller,
        MemberInitExpression memberInit)
    {
        bool completed = lineage.Count == current;

        if (completed
            && caller is MemberExpression m
            && m.Member.Name == lineage[^1].Member.Name)
        {
            _translations.Add(lineage[^1], caller);
            return true;
        }
        
        if (completed)
        {
            return false;
        }

        foreach (var binding in memberInit.Bindings)
        {
            if (binding is not MemberAssignment assignment)
            {
                continue;
            }

            if (TryAddFromAssignment(lineage, current, caller, assignment))
            {
                return true;
            }
        }

        return false;
    }


    private bool TryAddFromAssignment(
        IReadOnlyList<MemberExpression> lineage,
        int current,
        Expression caller,
        MemberAssignment assignment)
    {
        var access = Expression.MakeMemberAccess(caller, assignment.Member);
        current++;

        return assignment.Expression switch
        {
            MemberExpression m => TryAddFromMember(lineage, current, access, m),
            MemberInitExpression i => TryAddFromMemberInit(lineage, current, access, i), 
            ConditionalExpression c => TryAddFromConditional(lineage, current, access, c),
            _ => false
        };
    }


    private bool TryAddFromMember(
        IReadOnlyList<MemberExpression> lineage,
        int current,
        MemberExpression caller,
        MemberExpression member)
    {
        string assignName = ExpressionHelpers.GetLineageName(member);

        string remainingLineage = string.Join(
            string.Empty,
            lineage.Select(m => m.Member.Name));

        if (assignName != remainingLineage)
        {
            return false;
        }

        foreach (var chain in lineage.Skip(current))
        {
            caller = Expression.MakeMemberAccess(caller, chain.Member);
        }

        _translations.Add(lineage[^1], caller);

        return true;
    }


    private bool TryAddFromConditional(
        IReadOnlyList<MemberExpression> lineage,
        int current,
        MemberExpression caller,
        ConditionalExpression condition)
    {
        if (condition.IfTrue is MemberInitExpression trueInit)
        {
            return TryAddFromMemberInit(lineage, current, caller, trueInit);
        }

        if (condition.IfFalse is MemberInitExpression falseInit)
        {
            return TryAddFromMemberInit(lineage, current, caller, falseInit);
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