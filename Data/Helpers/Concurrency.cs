using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;


namespace MTGViewer.Data.Concurrency
{
    public class Concurrent
    {
        [ConcurrencyCheck]
        public Guid LiteToken { get; set; } = Guid.NewGuid();

        [Timestamp]
        public byte[] SqlToken { get; set; }
    }


    internal static class ConcurrencyExtensions
    {
        public static void MatchToken(
            this CardDbContext context, Concurrent current, PropertyValues dbProps)
        {
            if (dbProps == null)
            {
                return;
            }

            if (context.Database.IsSqlite())
            {
                var tokenProp = context.Entry(current)
                    .Property(c => c.LiteToken);

                tokenProp.OriginalValue = dbProps.GetValue<Guid>(tokenProp.Metadata);
            }

            else if (context.Database.IsSqlServer())
            {
                var tokenProp = context.Entry(current)
                    .Property(c => c.SqlToken);

                tokenProp.OriginalValue = dbProps.GetValue<byte[]>(tokenProp.Metadata);
            }

        }


        public static void MatchToken<E>(this CardDbContext context, E current, E dbValues)
            where E : Concurrent
        {
            context.MatchToken(
                current,
                context.Entry(dbValues).CurrentValues);
        }


        public static IEnumerable<EntityEntry<E>> Entries<E>(this DbUpdateConcurrencyException exception)
            where E : class
        {
            return exception.Entries
                .Where(en => en.Entity is E)
                .Select(en => en.Context.Entry((E)en.Entity));
        }


        public static IEnumerable<System.Type> GetConcurrentTypes()
        {
            var concurrentType = typeof(Concurrent);

            return concurrentType.Assembly.GetExportedTypes()
                .Where(t => 
                    t.IsSubclassOf(concurrentType) && !t.IsAbstract);
        }


        public static EntityTypeBuilder IgnoreExceptToken(
            this EntityTypeBuilder builder, Expression<Func<Concurrent, object>> property)
        {
            MemberExpression memberExpr;

            if (property.Body is UnaryExpression unaryExpr)
            {
                memberExpr = unaryExpr.Operand as MemberExpression;
            }
            else
            {
                memberExpr = property.Body as MemberExpression;
            }

            if (memberExpr is null)
            {
                return builder;
            }

            var exceptName = memberExpr.Member.Name;

            foreach(var entityProp in typeof(Concurrent).GetProperties())
            {
                if (entityProp.Name != exceptName)
                {
                    builder.Ignore(entityProp.Name);
                }
            }

            return builder;
        }

    }

}