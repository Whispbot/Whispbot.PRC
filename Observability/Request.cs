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
        public static async Task RecordEnd(this StreamEntry entry, RedisQueue<PRCRequest> queue, PRCRequest request, PRCResponse response)
        {
            DateTimeOffset end = await queue.GetServerTimeOffsetAsync();
            TimeSpan duration = end - entry.GetTimestamp();

            SentrySdk.Metrics.EmitDistribution(
                "prc.request.duration",
                duration.TotalMilliseconds,
                MeasurementUnit.Duration.Millisecond,
                [
                    new("success", response.success),
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
