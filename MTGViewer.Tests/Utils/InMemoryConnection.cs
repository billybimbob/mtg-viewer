using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;


namespace MTGViewer.Tests.Utils
{
    public sealed class InMemoryConnection : IAsyncDisposable, IDisposable
    {
        private readonly Lazy<SqliteConnection> _connection = new(() =>
        {
            var conn = new SqliteConnection("Filename=:memory:");
            conn.Open();

            return conn;
        });

        private readonly Lazy<string> _database = new(() => 
            "Test-Database-" + Guid.NewGuid());


        public DbConnection Connection => _connection.Value;

        public string Database => _database.Value;


        public async ValueTask DisposeAsync()
        {
            if (_connection.IsValueCreated)
            {
                await _connection.Value.DisposeAsync();
            }
        }


        public void Dispose()
        {
            if (_connection.IsValueCreated)
            {
                _connection.Value.Dispose();
            }
        }
    }
}