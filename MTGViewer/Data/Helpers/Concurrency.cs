using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;


namespace MTGViewer.Data.Concurrency
{
    public abstract class Concurrent
    {
        [ConcurrencyCheck]
        internal Guid LiteToken { get; set; } = Guid.NewGuid();

        [Timestamp]
        internal byte[] SqlToken { get; set; }
    }


    internal static class ConcurrencyExtensions
    {
        public static ModelBuilder SelectConcurrencyToken(this ModelBuilder builder, DatabaseFacade database)
        {
            if (database.IsSqlServer())
            {
                foreach(var concurrentType in ConcurrencyExtensions.GetConcurrentTypes())
                {
                    builder.Entity(concurrentType)
                        .Property(nameof(Concurrent.SqlToken));
                        // .IgnoreExceptToken(c => c.SqlToken);
                }
            }
            // else if (database.IsSqlite())
            else
            {
                foreach(var concurrentType in ConcurrencyExtensions.GetConcurrentTypes())
                {
                    builder.Entity(concurrentType)
                        .Property(nameof(Concurrent.LiteToken));
                        // .IgnoreExceptToken(c => c.LiteToken);
                }
            }

            return builder;
        }


        private static IEnumerable<System.Type> GetConcurrentTypes()
        {
            var concurrentType = typeof(Concurrent);

            return concurrentType.Assembly
                .GetExportedTypes()
                .Where(t => t.IsSubclassOf(concurrentType));
        }


        private static EntityTypeBuilder IgnoreExceptToken<T>(
            this EntityTypeBuilder builder, Expression<Func<Concurrent, T>> property)
        {
            var memberExpr = property.Body as MemberExpression;

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


        public static object GetToken(this CardDbContext context, Concurrent current)
        {
            // not great, since boxes
            if (context.Database.IsSqlServer())
            {
                return current.SqlToken;
            }
            else
            {
                return current.LiteToken;
            }
        }


        public static void MatchToken(
            this CardDbContext context, Concurrent current, PropertyValues dbProps)
        {
            if (dbProps == null)
            {
                return;
            }

            if (context.Database.IsSqlServer())
            {
                var tokenProp = context.Entry(current)
                    .Property(c => c.SqlToken);

                tokenProp.OriginalValue = dbProps.GetValue<byte[]>(tokenProp.Metadata);
            }
            // else if (context.Database.IsSqlite())
            else
            {
                var tokenProp = context.Entry(current)
                    .Property(c => c.LiteToken);

                tokenProp.OriginalValue = dbProps.GetValue<Guid>(tokenProp.Metadata);
            }
        }


        public static void MatchToken<E>(this CardDbContext context, E current, E dbValues)
            where E : Concurrent
        {
            context.MatchToken(
                current,
                context.Entry(dbValues).CurrentValues);
        }



        public static IEnumerable<EntityEntry<TEntity>> Entries<TEntity>(
            this DbUpdateConcurrencyException exception)
            where TEntity : class
        {
            return exception.Entries
                .Where(en => en.Entity is TEntity)
                .Select(en => en.Context.Entry((TEntity)en.Entity));
        }

    }

}