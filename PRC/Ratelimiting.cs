using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text;
using Whispbot.Databases;
using Whispbot.PRC.Messages;
using Whispbot.PRC.Scripts;

namespace Whispbot.PRC.PRC
{
    public static class Ratelimiting
    {
        public static string GlobalBucketName => API.HasGlobalAPIKey ? "public-app-get" : "unauthenticated-global";

        private static IDatabase? _db = null;
        private static IDatabase Database
        {
            get
            {
                _db ??= Redis.GetDatabase();
                return _db ?? throw new InvalidOperationException("Redis database is not available.");
            }
        }

        public static string GetBucketFromRequest(PRCRequest request, string? apiKey = null)
        {
            if (request.method == "POST" && request.serverId is not null && request.apiKey is not null)
            {
                return $"command-{(API.HasGlobalAPIKey ? $"{API.globalAPIKey}-" : "")}{apiKey ?? Encryption.BuildKey(request.apiKey, request.serverId)}";
            }
            else
            {
                return GlobalBucketName;
            }
        }

        private static readonly string _acquireScript   = Load.File("RatelimitAcquire");
        private static readonly string _releaseScript   = Load.File("RatelimitRelease");
        private static readonly string _ratelimitScript = Load.File("RatelimitOnLimit");

        public static async Task<(long remaining, long retryAfterMs)> AcquireAsync(string bucket, string requestId, int inflightTtlMs = 30_000)
        {
            var b = $"{{bucket:{bucket}}}";
            var res = await Database.ScriptEvaluateAsync(
                _acquireScript,
                [b, $"{b}:inflight"],
                [requestId, inflightTtlMs]
            );

            return ((long)res[0], (long)res[1]);
        }

        public static async Task<RedisResult> ReleaseAsync(string bucket, string requestId, int limit, long resetAtMs, int remaining)
        {
            var b = $"{{bucket:{bucket}}}";
            return await Database.ScriptEvaluateAsync(
                _releaseScript,
                [b, $"{b}:inflight"],
                [requestId, limit, resetAtMs, remaining]
            );
        }

        public static async Task<RedisResult> OnRatelimit(string bucket, string requestId, int limit, long resetAtMs)
        {
            var b = $"{{bucket:{bucket}}}";
            return await Database.ScriptEvaluateAsync(
                _ratelimitScript,
                [b, $"{b}:inflight"],
                [requestId, limit, resetAtMs]
            );
        }

        public static (string bucket, int limit, int remaining, long resetAtMs) GetRatelimitsFromRequest(HttpResponseMessage message)
        {
            string bucket = message.Headers.Contains("X-RateLimit-Bucket") ? message.Headers.GetValues("X-RateLimit-Bucket").FirstOrDefault() ?? GlobalBucketName : GlobalBucketName;
            int limit = int.Parse(message.Headers.GetValues("X-RateLimit-Limit").FirstOrDefault() ?? "0");
            int remaining = int.Parse(message.Headers.GetValues("X-RateLimit-Remaining").FirstOrDefault() ?? "0");
            long resetAtMs = long.Parse(message.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault() ?? "0") * 1000;

            return (bucket, limit, remaining, resetAtMs);
        }
    }
}
