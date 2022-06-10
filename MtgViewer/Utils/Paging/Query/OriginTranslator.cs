using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Query;

internal sealed class OriginTranslator<TOrigin, TEntity>
{
    private readonly ConstantExpression _origin;
    private readonly Expression? _selector;

    private readonly Dictionary<MemberExpression, MemberExpression> _translations;
    private readonly Dictionary<MemberExpression, bool> _nulls;

    public OriginTranslator(TOrigin? origin, Expression<Func<TEntity, TOrigin>>? selector)
    {
        var expressionEquality = ExpressionEqualityComparer.Instance;

        _origin = Expression.Constant(origin);

        _selector = SelectorVisitor.Instance.Visit(selector);

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

        if (TryAddFromProjection(member))
        {
            return true;
        }

        if (TryAddOriginChain(member))
        {
            return true;
        }

        if (TryAddOriginProperty(member))
        {
            return true;
        }

        return false;
    }

    private bool TryAddFromProjection(MemberExpression member)
    {
        if (_selector is not MemberInitExpression projection)
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

        return TryAddFromMemberInit(registration, translation, projection);
    }

    private bool TryAddFromMemberInit(
        Registration registration,
        Translation translation,
        MemberInitExpression memberInit)
    {
        if (translation is { IsFinished: true, Expression: MemberExpression m }
            && translation.IsMatch(registration))
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
            && translation.IsMatch(registration))
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

    private bool TryAddOriginChain(MemberExpression member)
    {
        using var e = GetPropertyChain(member).GetEnumerator();

        if (!e.MoveNext()
            || e.Current.DeclaringType is null
            || !e.Current.DeclaringType.IsInstanceOfType(_origin.Value))
        {
            return false;
        }

        var translation = Expression.Property(_origin, e.Current);

        while (e.MoveNext())
        {
            translation = Expression.Property(translation, e.Current);
        }

        _translations.Add(member, translation);
        return true;
    }

    private IEnumerable<PropertyInfo> GetPropertyChain(MemberExpression member)
    {
        if (_selector is not MemberExpression selectMember)
        {
            yield break;
        }

        using var e = ExpressionHelpers
            .GetLineage(selectMember)
            .Reverse()
            .GetEnumerator();

        bool isSelectorDone = e.MoveNext();

        var memberLineage = ExpressionHelpers
            .GetLineage(member)
            .Reverse();

        foreach (var m in memberLineage)
        {
            if (m.Member == e.Current?.Member && !isSelectorDone)
            {
                isSelectorDone = e.MoveNext();
                continue;
            }

            if (m.Member is not PropertyInfo p)
            {
                continue;
            }

            yield return p;
        }
    }

    private bool TryAddOriginProperty(MemberExpression member)
    {
        if (_origin.Type == member.Expression?.Type)
        {
            var originMember = Expression.MakeMemberAccess(_origin, member.Member);

            _translations.Add(member, originMember);

            return true;
        }

        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public;

        var property = _origin.Type
            .GetProperties(instance)
            .FirstOrDefault(p => p.PropertyType == member.Expression?.Type);

        if (property is null)
        {
            return false;
        }

        var translation = Expression.MakeMemberAccess(
            Expression.Property(_origin, property),
            member.Member);

        _translations.Add(member, translation);

        return true;
    }

    private sealed class Registration
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
        public int Target { get; init; }
        public int Progress { get; init; }

        public bool IsFinished => Progress == Target;

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

        public bool IsMatch(Registration registration)
        {
            if (Expression is not MemberExpression m)
            {
                return false;
            }

            return string.Equals(
                ExpressionHelpers.GetLineageName(m),
                registration.LineageName,
                StringComparison.Ordinal);
        }
    }

    private sealed class SelectorVisitor : ExpressionVisitor
    {
        private static SelectorVisitor? _instance;
        public static SelectorVisitor Instance => _instance ??= new();

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
}
