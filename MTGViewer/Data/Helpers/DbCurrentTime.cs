using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MTGViewer.Data;

public static class DbCurrentTime
{
    public static string GetCurrentTime(this DatabaseFacade database)
    {
        // TODO; add more database cases

        if (database.IsSqlServer())
        {
            return "getdate()";
        }
        else if (database.IsNpgsql())
        {
            return "now()";
        }
        // else if (database.IsSqlite())
        else
        {
            return "datetime('now', 'localtime')";
        }
    }
}