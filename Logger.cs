using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Whispbot.PRC
{
    public static class Logger
    {
        public static readonly bool isDev = Environment.GetEnvironmentVariable("RAILWAY_REPLICA_ID") is null;
        private static readonly string LOCAL_TEMPLATE = "[{Timestamp:HH:mm:ss.fff}][{Level:u4}]{Thread} {Message:lj} {Data}{NewLine}{Exception}";
        private static readonly string RAILWAY_TEMPLATE = "{{\"message\": \"{Message:lj}\", \"thread\": \"{Thread}\", \"level\": \"{Level:u4}\", \"data\": {Data}, \"error\": \"{Exception}\"}}{NewLine}";

        public static void Initialize()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(LogEventLevel.Verbose)
                .Enrich.With(new LogEnricher())
                .WriteTo.Console(
                    outputTemplate: isDev ? LOCAL_TEMPLATE : RAILWAY_TEMPLATE,
                    theme: SystemConsoleTheme.Colored)
              .CreateLogger();

            Log.Verbose("Logger initialized");
        }

        public static void Shutdown()
        {
            Log.CloseAndFlush();
        }

        public static ILogger WithData(object data)
        {
            return Log.ForContext("Data", JsonConvert.SerializeObject(data));
        }
    }

    public class LogEnricher: ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (!logEvent.Properties.ContainsKey("Data"))
                logEvent.AddPropertyIfAbsent(new LogEventProperty("Data", new ScalarValue(null)));

            string threadName = Thread.CurrentThread.Name ?? "<unknown>";
            string formatedThreadName = Logger.isDev ? $"[{threadName}]".PadRight(12, ' ') : threadName;
            logEvent.AddPropertyIfAbsent(new LogEventProperty("Thread", new ScalarValue(formatedThreadName)));
        }
    }
}
