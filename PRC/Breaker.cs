using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Whispbot.PRC.PRC
{
    public static class Breaker
    {
        public static readonly double ERROR_RATE_THRESHOLD = 0.2;
        public static readonly double CHANCE_TO_RANDOM_REQUEST = 0.1;

        private static IDatabase? _database = null;

        public static int requestsLastWindow = 0;
        public static int errorsLastWindow = 0;

        public static int requestsCurrentWindow = 0;
        public static int errorsCurrentWindow = 0;

        public static readonly TimeSpan window = TimeSpan.FromSeconds(10);

        public static double windowStart = CurrentWindow;
        public static double CurrentWindow => Math.Floor(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / window.TotalSeconds);
        public static double WindowEnd => windowStart + 1;
        public static double WindowEndUnixSeconds => WindowEnd * window.TotalSeconds;
        public static bool NextWindowStarted => DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= WindowEndUnixSeconds;

        public static double WindowProgress => (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (windowStart * window.TotalSeconds)) / window.TotalSeconds;

        public static double LastWindowErrorRate => requestsLastWindow == 0 ? 0 : (double)errorsLastWindow / requestsLastWindow;
        public static double CurrentWindowErrorRate => requestsCurrentWindow == 0 ? 0 : (double)errorsCurrentWindow / requestsCurrentWindow;
        public static double AverageErrorRate => (CurrentWindowErrorRate * WindowProgress) + (LastWindowErrorRate * (1 - WindowProgress));

        private static Random _random = new();

        // 0.32 < (0.1 / 0.3) = 0.333 | 0.32 < (0.1 / 0.8) = 0.125
        public static bool IsOpen => AverageErrorRate < ERROR_RATE_THRESHOLD || (_random.NextDouble() < Math.Min(CHANCE_TO_RANDOM_REQUEST / AverageErrorRate, 1));

        public static void Init(IDatabase database)
        {
            _database = database;
        }

        public static void StartNextWindow()
        {
            requestsLastWindow = requestsCurrentWindow;
            errorsLastWindow = errorsCurrentWindow;
            requestsCurrentWindow = 0;
            errorsCurrentWindow = 0;
            windowStart = CurrentWindow;

            ReportMetrics();
        }

        public static void RecordRequest(bool isError)
        {
            if (NextWindowStarted) StartNextWindow();

            requestsCurrentWindow++;
            if (isError) errorsCurrentWindow++;
        }

        public static void ReportMetrics()
        {
            // Goes to sentry so that we can track the error rate over time
            SentrySdk.Metrics.EmitCounter("breaker.requests", requestsLastWindow);
            SentrySdk.Metrics.EmitCounter("breaker.errors", errorsLastWindow);
        }
    }
}
