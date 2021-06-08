using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace MTGViewer.Services
{
    public class DataCacheService
    {

        private readonly ILogger<DataCacheService> _logger;
        private readonly IMemoryCache _cache;
        private readonly MemoryCacheEntryOptions _options;

        public DataCacheService(ILogger<DataCacheService> logger)
        {
            _logger = logger;
            _cache = new MemoryCache( new MemoryCacheOptions 
            {
                SizeLimit = 100
                // should auto evict from limit
            });

            _options = new MemoryCacheEntryOptions
            {
                Size = 1,
            };
        }

        public object this[object key]
        {
            set
            {
                if (key == null)
                {
                    return;
                }

                _cache.Set(key, value, _options);
                // _logger.LogInformation($"there are {(_cache as MemoryCache)?.Count} entries in the cache");
            }
        }

        public bool TryGetValue<T>(object key, out T value)
        {
            return _cache.TryGetValue(key, out value);
        }

    }
}