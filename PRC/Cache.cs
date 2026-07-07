using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.Databases;
using Whispbot.PRC.Messages;
using Whispbot.PRC.Scripts;

namespace Whispbot.PRC.PRC
{
    public static class Cache
    {
        public static readonly Dictionary<string, (DateTimeOffset, PRCResponse)> localCache = [];

        public static string GetCacheKey(PRCRequest request) => $"ERLC:{request.endpoint}:{request.serverId ?? "global"}";

        public static long GetCacheDuration(PRCResponse response) => response.success ? 60 : 10;

        public static async Task<PRCResponse?> GetCache(PRCRequest request)
        {
            if (request.method != "GET") return null;

            string key = GetCacheKey(request);
            if (localCache.TryGetValue(key, out var data))
            {
                var (expires, response) = data;

                if (expires > DateTimeOffset.UtcNow)
                {
                    return response;
                }
                else
                {
                    localCache.Remove(key);
                }
            }

            var redis = Redis.GetDatabase();
            if (redis is null) return null;

            var cachedValue = await redis.StringGetAsync(key);
            var cachedResponse = cachedValue.HasValue ? JsonConvert.DeserializeObject<PRCResponse>(cachedValue.ToString()) : null;

            if (cachedResponse is not null) localCache.Add(key, (DateTimeOffset.FromUnixTimeMilliseconds(cachedResponse.cachedAtMs).AddSeconds(GetCacheDuration(cachedResponse)), cachedResponse));

            return cachedResponse;
        }

        public static async Task SetCache(PRCRequest request, PRCResponse response)
        {
            if (request.method != "GET") return;
            var redis = Redis.GetDatabase();
            if (redis is null) return;

            string key = GetCacheKey(request);
            long cacheDuration = GetCacheDuration(response);
            
            var expires = DateTimeOffset.UtcNow.AddSeconds(cacheDuration);

            response.cachedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await redis.StringSetAsync(key, JsonConvert.SerializeObject(response), TimeSpan.FromSeconds(cacheDuration));
            localCache.Remove(key);
            localCache.Add(key, (expires, response));
        }
    }
}
