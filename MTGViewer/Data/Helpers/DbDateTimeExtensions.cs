using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

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
}