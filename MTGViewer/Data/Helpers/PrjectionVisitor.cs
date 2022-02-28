// using System.Reflection;
// using Microsoft.EntityFrameworkCore.Query;
// namespace System.Linq.Expressions;

// internal class FindProjection<TSource, TResult> : ExpressionVisitor
// {
//     private static MethodInfo? _selectMethod;

//     protected override Expression VisitMethodCall(MethodCallExpression node)
//     {
//         if (typeof(TSource) == typeof(TResult))
//         {
//             return node;
//         }

//         if (node.Arguments.ElementAtOrDefault(0) is not Expression caller)
//         {
//             return node;
//         }

//         _selectMethod ??= ExpressionConstants.QueryableSelect
//             .MakeGenericMethod(typeof(TSource), typeof(TResult));

//         if (node.Method == _selectMethod
//             && node.Arguments.ElementAtOrDefault(1) is UnaryExpression unary
//             && unary.NodeType is ExpressionType.Quote
//             && unary.Operand is Expression<Func<TSource, TResult>> projection)
//         {
//             return projection;
//         }

//         return Visit(caller);
//     }


//     private static FindProjection<TSource, TResult>? _instance;

//     public static ExpressionVisitor Instance => _instance ??= new();
// }


// internal class RemoveProjection<TSource> : ExpressionVisitor
// {
//     private Type? _queryType;

//     protected override Expression VisitMethodCall(MethodCallExpression node)
//     {
//         if (node.Object is not null)
//         {
//             return node;
//         }

//         _queryType ??= typeof(IQueryable<>).MakeGenericType(typeof(TSource));

//         if (node.Method.ReturnType.IsAssignableTo(_queryType))
//         {
//             return node;
//         }

//         if (node.Arguments.ElementAtOrDefault(0) is not Expression caller)
//         {
//             return ExpressionConstants.Null;
//         }

//         return base.Visit(caller);
//     }


//     private static RemoveProjection<TSource>? _instance;

//     public static ExpressionVisitor Instance => _instance ??= new();
// }



// internal class OriginalCallerVisitor : ExpressionVisitor
// {
//     protected override Expression VisitMethodCall(MethodCallExpression node)
//     {
//         if (node.Arguments.ElementAtOrDefault(0) is not Expression caller)
//         {
//             return node;
//         }

//         if (caller is QueryRootExpression)
//         {
//             return caller;
//         }

//         return Visit(caller);
//     }


//     private static OriginalCallerVisitor? _instance;

//     public static ExpressionVisitor Instance => _instance ??= new();
// }