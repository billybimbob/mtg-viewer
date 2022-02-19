using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MTGViewer.Services;

internal enum DatabaseContext
{
    Card,
    User
}


public sealed class DatabaseOptions
{
    public const string SqlServer = "SqlServer";
    public const string Postgresql = "Postgresql";
    public const string Sqlite = "Sqlite";
    public const string InMemory = "InMemory";

    public string? Provider { get; set; }
    public string? CardRedirect { get; set; }
    public string? UserRedirect { get; set; }


    public static DatabaseOptions Bind(IConfiguration configuration)
    {
        var options = new DatabaseOptions();

        configuration.GetSection(nameof(DatabaseOptions)).Bind(options);

        return options;
    }

    internal string GetConnectionString(IConfiguration configuration, DatabaseContext context)
    {
        if (context is DatabaseContext.Card && CardRedirect is null)
        {
            return configuration.GetConnectionString(Provider ?? Sqlite);
        }

        if (context is DatabaseContext.User && CardRedirect is null && UserRedirect is null)
        {
            return configuration.GetConnectionString(Provider ?? Sqlite);
        }

        string? redirect = context is DatabaseContext.Card
            ? CardRedirect
            : UserRedirect ?? CardRedirect;

        return configuration[redirect];
    }
}