using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using MTGViewer.Data.Infrastructure;

namespace MTGViewer.Data;

public partial class CardDbContext
{
    protected ModelBuilder SelectConcurrencyToken(ModelBuilder builder)
    {
        var concurrentTypes = typeof(CardDbContext)
            .Assembly
            .ExportedTypes
            .Where(t => t.IsSubclassOf(typeof(Concurrent)));

        foreach (var type in concurrentTypes)
        {
            AddConcurrentProperty(builder, type);
        }

        return builder;
    }

    private void AddConcurrentProperty(ModelBuilder builder, Type entityType)
    {
        var entity = builder.Entity(entityType);

        if (Database.IsSqlServer())
        {
            entity.Property(nameof(Concurrent.Version));
        }
        else if (Database.IsNpgsql())
        {
            entity.UseXminAsConcurrencyToken();
        }
        // else if (Database.IsSqlite())
        else
        {
            entity.Property(nameof(Concurrent.Stamp));
        }
    }

    public object GetToken(Concurrent current)
    {
        // not great, since boxes

        if (Database.IsSqlServer())
        {
            return current.Version;
        }
        else if (Database.IsNpgsql())
        {
            return current.xmin;
        }
        else
        {
            return current.Stamp;
        }
    }

    public void MatchToken(Concurrent current, PropertyValues dbProperties)
    {
        if (dbProperties == null)
        {
            return;
        }

        if (Database.IsSqlServer())
        {
            var tokenProp = Entry(current)
                .Property(c => c.Version);

            tokenProp.OriginalValue = dbProperties
                .GetValue<byte[]>(tokenProp.Metadata);
        }

        else if (Database.IsNpgsql())
        {
            var tokenProp = Entry(current)
                .Property(c => c.xmin);

            tokenProp.OriginalValue = dbProperties
                .GetValue<uint>(tokenProp.Metadata);

            tokenProp.CurrentValue = dbProperties
                .GetValue<uint>(tokenProp.Metadata);
        }

        // else if (Database.IsSqlite())
        else
        {
            var tokenProp = Entry(current)
                .Property(c => c.Stamp);

            tokenProp.OriginalValue = dbProperties
                .GetValue<Guid>(tokenProp.Metadata);
        }
    }

    public void MatchToken<TEntity>(TEntity current, TEntity db)
        where TEntity : Concurrent
    {
        var dbProperties = Entry(db).CurrentValues;

        MatchToken(current, dbProperties);
    }

    internal void CopyToken(ConcurrentDto current, Concurrent db)
    {
        if (Database.IsSqlServer())
        {
            current.Version = db.Version;
        }
        else if (Database.IsNpgsql())
        {
            current.xmin = db.xmin;
        }
        // else if (Database.IsSqlite())
        else
        {
            current.Stamp = db.Stamp;
        }
    }
}
