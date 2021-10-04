using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;


namespace MTGViewer.Tests.Utils
{
    public sealed class EmptyProvider : IAsyncDisposable, IDisposable
    {
        public const string TestDB = "Test Database";

        private readonly ServiceProvider _provider;

        public EmptyProvider()
        {
            _provider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();
        }


        public IServiceProvider Provider => _provider;


        public ValueTask DisposeAsync() => _provider.DisposeAsync();

        public void Dispose() => _provider.Dispose();
    }
}