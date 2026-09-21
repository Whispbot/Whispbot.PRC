using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Whispbot.PRC.Databases
{
    public static class SentryConnection
    {
        private static readonly string? _replica = Environment.GetEnvironmentVariable("RAILWAY_REPLICA_ID");
        private static readonly string _releaseId =
            Environment.GetEnvironmentVariable("RAILWAY_DEPLOYMENT_ID")?.Split("-")[0] 
            ?? $"dev{Random.Shared.Next(65_536, 1_048_575):X5}";

        public static void Init()
        {
            string? sentry_dsn = Environment.GetEnvironmentVariable("SENTRY_DSN");

            if (sentry_dsn is null)
            {
                Log.Error("Could not connect to sentry, no SENTRY_DSN environment variable.");
                return;
            }

            try
            {
                SentrySdk.Init(options =>
                {
                    options.Dsn = sentry_dsn;

                    options.Debug = false;

                    options.AutoSessionTracking = true;

                    options.SetBeforeSendMetric(static metric =>
                    {
                        if (_replica is not null) metric.SetAttribute("replica", _replica);

                        return metric;
                    });

                    options.Release = $"whispbot.prc@{Assembly.GetEntryAssembly()?.GetName().Version}+{_releaseId}";
                    options.Environment = _replica is not null ? "production" : "development";
                });
                Log.Information("Initialized sentry");
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to initialize sentry, not fatal");
                Log.Warning(ex.Message);
            }
        }
    }
}
