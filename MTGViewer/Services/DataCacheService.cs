using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Services
{
    public class DataCacheService
    {
        private readonly ILogger<DataCacheService> _logger;
        private readonly IMemoryCache _cache;
        private readonly MemoryCacheEntryOptions _options;

        public DataCacheService(IConfiguration config, ILogger<DataCacheService> logger)
        {
            _logger = logger;
            _cache = new MemoryCache(new MemoryCacheOptions
            {
                // should auto evict from limit
                SizeLimit = config.GetValue("CacheLimit", 100L)
            });

            _options = new()
            {
                Size = 1,
            };
        }

        public object this[object key]
        {
            set
            {
                if (key is null)
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