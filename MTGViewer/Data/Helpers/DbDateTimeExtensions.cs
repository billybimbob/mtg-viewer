using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MTGViewer.Data;

public static class DbDateTimeExtensions
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


    public static PropertyBuilder<DateTime> UtcToLocal(this PropertyBuilder<DateTime> timeProperty)
    {
        return timeProperty.HasConversion(
            local => DateTime
                .SpecifyKind(local, DateTimeKind.Local)
                .ToUniversalTime(), 

            utc => DateTime
                .SpecifyKind(utc, DateTimeKind.Utc)
                .ToLocalTime());
    }
}