using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

public sealed class OriginTranslator<TOrigin, TEntity>
{
    private const StringComparison Ordinal = StringComparison.Ordinal;

    private readonly Dictionary<MemberExpression, MemberExpression> _translations;
    private readonly Dictionary<MemberExpression, bool> _nulls;

    private readonly ConstantExpression _origin;
    private readonly MemberExpression? _selector;
    private readonly MemberInitExpression? _projection;


    public OriginTranslator(TOrigin origin, Expression<Func<TEntity, TOrigin>>? selector)
    {
        _translations = new(ExpressionEqualityComparer.Instance);
        _nulls = new(ExpressionEqualityComparer.Instance);

        var visitSelect = SelectorVisitor.Instance.Visit(selector);

        _origin = Expression.Constant(origin);

        _selector = visitSelect as MemberExpression;
        _projection = visitSelect as MemberInitExpression;
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


    public bool IsParentNull(MemberExpression member)
    {
        if (member is not MemberExpression chain)
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
            .GetLineage(translation as MemberExpression)
            .Skip(1)
            .Reverse()
            .GetEnumerator();

        var reference = _origin.Value;

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

        int target = ExpressionHelpers
            .GetLineage(member)
            .Count();

        if (target == 0)
        {
            return false;
        }

        var registration = new Registration(member);

        var translation = new Translation { Expression = _origin, Target = target };

        return TryAddFromMemberInit(registration, translation, _projection);
    }


    private class Registration
    {
        public MemberExpression Expression { get; }
        public string LineageName { get; }

        public Registration(MemberExpression member)
        {
            Expression = member;
            LineageName = ExpressionHelpers.GetLineageName(member);
        }
    }


    private readonly struct Translation
    {
        public Expression? Expression { get; init; }
        public int Progress { get; init; }
        public int Target { get; init; }

        public bool IsFinished => Progress == Target;

        public Translation(Expression expression, int target)
        {
            Expression = expression;
            Target = target;
            Progress = 0;
        }

        public Translation MakeAccess(MemberInfo member)
        {
            var access = Expression.MakeMemberAccess(Expression, member);

            return this with
            {
                Expression = access,
                Target = Target,
                Progress = Progress + 1
            };
        }

        public string? GetName()
        {
            if (Expression is not MemberExpression m)
            {
                return null;
            }

            return ExpressionHelpers.GetLineageName(m);
        }
    }


    private bool TryAddFromMemberInit(
        Registration registration,
        Translation translation,
        MemberInitExpression memberInit)
    {
        if (translation is { IsFinished: true, Expression: MemberExpression m }
            && string.Equals(
                translation.GetName(),
                registration.LineageName, Ordinal))
        {
            _translations.Add(registration.Expression, m);
            return true;
        }

        if (translation.IsFinished)
        {
            return false;
        }

        foreach (var binding in memberInit.Bindings)
        {
            if (binding is MemberAssignment assignment
                && TryAddFromAssignment(registration, translation, assignment))
            {
                return true;
            }
        }

        return false;
    }


    private bool TryAddFromAssignment(
        Registration registration,
        Translation translation,
        MemberAssignment assignment)
    {
        translation = translation.MakeAccess(assignment.Member);

        return assignment.Expression switch
        {
            MemberExpression => TryAddFromMember(registration, translation),
            MemberInitExpression i => TryAddFromMemberInit(registration, translation, i), 
            ConditionalExpression c => TryAddFromConditional(registration, translation, c),
            _ => false
        };
    }


    private bool TryAddFromMember(Registration registration, Translation translation)
    {
        if (translation.Expression is MemberExpression m
            && string.Equals(
                translation.GetName(),
                registration.LineageName, Ordinal))
        {
            _translations.Add(registration.Expression, m);

            return true;
        }

        return false;
    }


    private bool TryAddFromConditional(
        Registration registration,
        Translation translation,
        ConditionalExpression condition)
    {
        if (condition.IfTrue is MemberInitExpression trueInit)
        {
            return TryAddFromMemberInit(registration, translation, trueInit);
        }

        if (condition.IfFalse is MemberInitExpression falseInit)
        {
            return TryAddFromMemberInit(registration, translation, falseInit);
        }

        if (condition.IfTrue is MemberExpression
            || condition.IfFalse is MemberExpression)
        {
            return TryAddFromMember(registration, translation);
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