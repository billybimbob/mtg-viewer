using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MTGViewer.Data.Configuration;

public static class ModelBuilderExtensions
{
    public static string GetUtcTime(this DatabaseFacade database)
    {
        // TODO; add more database cases

        if (database.IsSqlServer())
        {
            return "getutcdate()";
        }
        else if (database.IsNpgsql())
        {
            return "timezone('UTC', now())";
        }
        // else if (database.IsSqlite())
        else
        {
            // uses utc by default: https://sqlite.org/lang_datefunc.html
            return "datetime('now')";
        }
    }

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
            _ = entity.Property(nameof(Concurrent.Version));
        }
        else if (database.IsNpgsql())
        {
            _ = entity.UseXminAsConcurrencyToken();
        }
        // else if (database.IsSqlite())
        else
        {
            _ = entity.Property(nameof(Concurrent.Stamp));
        }
    }
}
