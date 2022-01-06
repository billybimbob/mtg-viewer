using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MTGViewer.Services;

public class FixedCache
{
    private readonly ILogger<FixedCache> _logger;
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _options;

    public FixedCache(IMemoryCache cache, ILogger<FixedCache> logger)
    {
        _logger = logger;
        _cache = cache;
        _options = new()
        { 
            Size = 1,
            // TODO: add config expire
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5)
        };
    }


    public object this[string key]
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

    public bool TryGetValue<TValue>(string key, out TValue value)
    {
        return _cache.TryGetValue(key, out value);
    }


    public void Remove(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        _cache.Remove(key);
    }


    public Task<TValue> GetOrCreateAsync<TValue>(string key, Func<Task<TValue>> factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        return _cache.GetOrCreateAsync(key, entry =>
        {
            entry.SetOptions(_options);
            return factory();
        });
    }
}