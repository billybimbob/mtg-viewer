using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;


namespace MTGViewer.Tests.Utils
{
    public sealed class InMemoryConnection : IAsyncDisposable, IDisposable
    {
        private SqliteConnection _connection;
        private string _database;

        public DbConnection Connection
        {
            get
            {
                if (_connection is null)
                {
                    _connection = new("Filename=:memory:");
                    _connection.Open();
                }

                return _connection;
            }
        }

        public string Database
        {
            get
            {
                _database ??= "Test-Database-" + Guid.NewGuid();

                return _database;
            }
        }


        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }


        public void Dispose() => _connection?.Dispose();
    }
}