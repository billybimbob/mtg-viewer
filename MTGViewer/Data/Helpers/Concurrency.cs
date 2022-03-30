using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MTGViewer.Data.Concurrency;

// each internal property is ignored by convention
public abstract class Concurrent
{
    [ConcurrencyCheck]
    internal Guid Stamp { get; set; }

    [Timestamp]
    internal byte[] SqlToken { get; set; } = Array.Empty<byte>();

    internal uint xmin { get; set; }
}


internal abstract class ConcurrentDto
{
    [JsonInclude]
    public Guid Stamp { get; set; }

    [JsonInclude]
    public byte[] SqlToken { get; set; } = Array.Empty<byte>();

    [JsonInclude]
    public uint xmin { get; set; }
}


internal static class ConcurrencyExtensions
{
    public static ModelBuilder SelectConcurrencyToken(this ModelBuilder builder, DatabaseFacade database)
    {
        foreach (var concurrentType in GetConcurrentTypes())
        {
            builder.Entity(concurrentType)
                .AddConcurrentProperty(database);
        }

        return builder;
    }


    private static IEnumerable<Type> GetConcurrentTypes()
    {
        var concurrentType = typeof(Concurrent);

        return concurrentType.Assembly.ExportedTypes
            .Where(t => t.IsSubclassOf(concurrentType));
    }


    private static void AddConcurrentProperty(this EntityTypeBuilder entity, DatabaseFacade database)
    {
        if (database.IsSqlServer())
        {
            entity.Property(nameof(Concurrent.SqlToken));
        }
        else if (database.IsNpgsql())
        {
            entity.UseXminAsConcurrencyToken();
        }
        // else if (database.IsSqlite())
        else
        {
            entity.Property(nameof(Concurrent.Stamp));
        }
    }


    public static object GetToken(this CardDbContext context, Concurrent current)
    {
        // not great, since boxes
        if (context.Database.IsSqlServer())
        {
            return current.SqlToken;
        }
        else if (context.Database.IsNpgsql())
        {
            return current.xmin;
        }
        else
        {
            return current.Stamp;
        }
    }


    public static void MatchToken(
        this CardDbContext context, Concurrent current, PropertyValues dbProps)
    {
        if (dbProps == null)
        {
            return;
        }

        var db = context.Database;
        var entry = context.Entry(current);

        if (db.IsSqlServer())
        {
            var tokenProp = entry
                .Property(c => c.SqlToken);

            tokenProp.OriginalValue = dbProps
                .GetValue<byte[]>(tokenProp.Metadata);
        }

        else if (db.IsNpgsql())
        {
            var tokenProp = entry
                .Property(c => c.xmin);

            tokenProp.OriginalValue = dbProps
                .GetValue<uint>(tokenProp.Metadata);

            tokenProp.CurrentValue = dbProps
                .GetValue<uint>(tokenProp.Metadata);
        }

        // else if (context.Database.IsSqlite())
        else
        {
            var tokenProp = entry
                .Property(c => c.Stamp);

            tokenProp.OriginalValue = dbProps
                .GetValue<Guid>(tokenProp.Metadata);
        }
    }


    public static void MatchToken<E>(this CardDbContext context, E current, E dbValues)
        where E : Concurrent
    {
        context.MatchToken(
            current,
            context.Entry(dbValues).CurrentValues);
    }


    public static void CopyToken(this CardDbContext context, ConcurrentDto current, Concurrent db)
    {
        if (context.Database.IsSqlServer())
        {
            current.SqlToken = db.SqlToken;
        }
        else if (context.Database.IsNpgsql())
        {
            current.xmin = db.xmin;
        }
        // else if (context.Database.IsSqlite())
        else
        {
            current.Stamp = db.Stamp;
        }
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