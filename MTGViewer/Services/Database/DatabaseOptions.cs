using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MTGViewer.Services;

public enum DatabaseContext
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

    private IConfiguration? Configuration { get; init; }


    public static DatabaseOptions Bind(IConfiguration configuration)
    {
        var options = new DatabaseOptions
        {
            Configuration = configuration
        };

        configuration.GetSection(nameof(DatabaseOptions)).Bind(options);

        return options;
    }


    public string GetConnectionString(DatabaseContext context)
    {
        if (Configuration is null)
        {
            throw new InvalidOperationException("Configuration is missing");
        }

        if (context is DatabaseContext.Card && CardRedirect is null)
        {
            return Configuration.GetConnectionString(Provider ?? Sqlite);
        }

        if (context is DatabaseContext.User
            && this is { CardRedirect: null, UserRedirect: null })
        {
            return Configuration.GetConnectionString(Provider ?? Sqlite);
        }

        string? redirect = context is DatabaseContext.Card
            ? CardRedirect
            : UserRedirect ?? CardRedirect;

        return Configuration[redirect];
    }
}