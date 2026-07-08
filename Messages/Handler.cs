using Newtonsoft.Json;
using Serilog;
using StackExchange.Redis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Whispbot.PRC.PRC;
using YellowMacaroni.Redis.Queue;

namespace Whispbot.PRC.Messages
{
    public static class Handler
    {
        public static readonly List<ErrorCode> activateBreakerCodes = [
            ErrorCode.InternalServerError,
            ErrorCode.Unknown,
            ErrorCode.GlobalKeyInvalid,
            ErrorCode.RobloxServerError
        ]; 

        public static async Task<(ErrorCode error, object? data, long retryAfterMs)> OnMessage(RedisQueue<PRCRequest> queue, StreamEntry entry, PRCRequest message)
        {
            string requestId = entry.GetId();
            string logId = entry.GetShortId();

            try
            {
                Log.Debug($"{logId}: Running {message.method} {API.GetPath(message.endpoint)} for server {message.serverId}");

                if (!Breaker.IsOpen)
                {
                    Log.Warning($"{logId}: Circuit breaker is open");
                    await queue.Requeue(entry, TimeSpan.FromSeconds(20 * Breaker.AverageErrorRate));
                    return (ErrorCode.CircuitBreakerOpen, null, -1);
                }

                string? apiKey = message.FullAPIKey;

                string bucket = Ratelimiting.GetBucketFromRequest(message, apiKey);
                var (remaining, retryAfterMs) = await Ratelimiting.AcquireAsync(bucket, requestId);

                if (remaining < 0)
                {
                    Log.Debug($"{logId}: Rate limit exceeded for bucket");
                    await queue.Requeue(entry, TimeSpan.FromMilliseconds(retryAfterMs));
                    return (ErrorCode.Ratelimited, null, retryAfterMs);
                }

                var response = await API.Request(message);

                var (requestBucket, limit, remainingAfter, resetAtMs) = Ratelimiting.GetRatelimitsFromRequest(response);

                if (requestBucket != bucket)
                {
                    Log.Warning($"{logId}: Ratelimit bucket mismatch. Expected: {bucket}, Actual: {requestBucket}");
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await Ratelimiting.OnRatelimit(bucket, requestId, limit, resetAtMs);
                }
                else
                {
                    await Ratelimiting.ReleaseAsync(bucket, requestId, limit, resetAtMs, remainingAfter);
                }

                if (response.IsSuccessStatusCode)
                {
                    Breaker.RecordRequest(false);

                    Log.Debug($"{logId}: Request processed successfully, {remainingAfter}/{limit} for {Math.Round((double)(resetAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000, 1)}s");

                    return (ErrorCode.Nothing, JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync()), -1);
                }
                else
                {
                    var error = JsonConvert.DeserializeObject<PRCError>(await response.Content.ReadAsStringAsync()) 
                        ?? throw new JsonSerializationException("Failed to parse error response");

                    Breaker.RecordRequest(activateBreakerCodes.Contains(error.code) || (int)response.StatusCode >= 500);

                    Log.Error($"{logId}: Request failed: {error.code} {error.message}");

                    return (error.code, new
                    {
                        error.message,
                        docs = error.learn_more_and_docs
                    }, -1);
                }
            } 
            catch (Exception ex)
            {
                Log.Error(ex, $"{logId}: An unexpected error occurred");

                return (ErrorCode.Unknown, new
                {
                    errorId = SentrySdk.CaptureException(ex).ToString()
                }, -1);
            }
        }
    }
}
