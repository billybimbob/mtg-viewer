using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MTGViewer.Tests.Utils;


public sealed class InMemoryConnection : IAsyncDisposable
{
    private readonly Lazy<SqliteConnection> _connection = new(() =>
    {
        var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();

        return conn;
    });

    public DbConnection Connection => _connection.Value;

    public string Database { get; } = "Test-Database-" + Guid.NewGuid();


    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated)
        {
            await _connection.Value.DisposeAsync();
        }
    }
}