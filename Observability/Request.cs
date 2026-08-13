using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.Messages;
using Whispbot.PRC.PRC;
using YellowMacaroni.Redis.Queue;

namespace Whispbot.PRC.O11y
{
    public static class RequestObervability
    {
        private static List<ErrorCode> _badErrorCodes = [
            ErrorCode.InternalServerError,
            ErrorCode.RobloxServerError,
            ErrorCode.CircuitBreakerOpen,
            ErrorCode.GlobalKeyInvalid,
            ErrorCode.NoServerKeyProvided,
            ErrorCode.ResourceRestricted,
            ErrorCode.Ratelimited,
            ErrorCode.ServerKeyMalformed,
            ErrorCode.Unknown
        ];

        public static async Task RecordEnd(this StreamEntry entry, RedisQueue<PRCRequest> queue, PRCRequest request, PRCResponse response)
        {
            TimeSpan duration = await entry.GetDurationSinceEnqueueAsync(queue);

            SentrySdk.Metrics.EmitDistribution(
                "prc.request.duration",
                duration.TotalMilliseconds,
                MeasurementUnit.Duration.Millisecond,
                [
                    new("success", response.success),
                    new("error", _badErrorCodes.Contains(response.error)),
                    new("code", response.error),
                    new("cached", response.cachedAtMs != -1),
                    new("endpoint", API.GetPath(request.endpoint)),
                    new("env", Environment.GetEnvironmentVariable("RAILWAY_REPLICA_ID") is not null ? Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT_NAME") ?? "production" : "dev"),
                    new("replica", Environment.GetEnvironmentVariable("RAILWAY_REPLICA_ID") ?? "dev"),
                    new("server.id", request.serverId ?? "global")
                ]
            );
        }
    }
}
