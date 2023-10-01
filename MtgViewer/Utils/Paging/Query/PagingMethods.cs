using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace EntityFrameworkCore.Paging.Query;

public static class PagingMethods
{
    public static MethodInfo SeekBy { get; }

    public static MethodInfo AfterReference { get; }

    public static MethodInfo AfterPredicate { get; }

    public static MethodInfo ToSeekList { get; }

    static PagingMethods()
    {
        var pagingTypeInfo = typeof(PagingExtensions).GetTypeInfo();

        SeekBy = pagingTypeInfo.GetDeclaredMethod(nameof(PagingExtensions.SeekBy))!;

        AfterReference = pagingTypeInfo
            .GetDeclaredMethods(nameof(PagingExtensions.After))
            .Single(mi => mi
                .GetParameters()
                .Any(pi => pi.Name == "origin"
                    && !pi.ParameterType.IsAssignableTo(typeof(Expression))));

        AfterPredicate = pagingTypeInfo
            .GetDeclaredMethods(nameof(PagingExtensions.After))
            .Single(mi => mi
                .GetParameters()
                .Any(pi => pi.Name == "originPredicate"
                    && pi.ParameterType.IsAssignableTo(typeof(Expression))));

        ToSeekList = pagingTypeInfo.GetDeclaredMethod(nameof(PagingExtensions.ToSeekList))!;
    }
}
