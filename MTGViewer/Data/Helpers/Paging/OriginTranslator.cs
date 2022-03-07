using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

public sealed class OriginTranslator
{
    private readonly ConstantExpression _origin;
    private readonly MemberExpression? _selector;

    private readonly Dictionary<MemberExpression, MemberExpression> _translations;

    public OriginTranslator(object origin, LambdaExpression? selector)
    {
        _origin = Expression.Constant(origin);

        _selector = SelectorVisitor.Visit(_origin.Type, selector);

        _translations = new(ExpressionEqualityComparer.Instance);
    }


    public MemberExpression Translate(MemberExpression member)
    {
        if (!_translations.TryGetValue(member, out var translation))
        {
            throw new ArgumentException(nameof(member));
        }

        return translation;
    }


    public bool IsRegistered(MemberExpression member)
    {
        return _translations.ContainsKey(member);
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

        if (TryAddFlat(member))
        {
            return true;
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

        if (_origin.Type.GetProperty(lineageName, member.Type) is PropertyInfo property)
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
        private readonly Type _origin;

        public SelectorVisitor(Type origin)
        {
            _origin = origin;
        }

        public static MemberExpression? Visit(Type origin, LambdaExpression? node)
        {
            var visitor = new SelectorVisitor(origin);

            return visitor.Visit(node) as MemberExpression;
        }

        protected override Expression VisitLambda<TFunc>(Expression<TFunc> node)
        {
            if (node.Parameters.Count == 1
                && node.Parameters[0].Type != _origin
                && node.Body.Type == _origin)
            {
                return Visit(node.Body);
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