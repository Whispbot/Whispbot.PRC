using Serilog;
using StackExchange.Redis;
using System.Runtime.InteropServices;
using Whispbot.Databases;
using Whispbot.PRC;
using Whispbot.PRC.Databases;
using Whispbot.PRC.Messages;
using Whispbot.PRC.PRC;
using Whispbot.PRC.Updates;
using YellowMacaroni.Redis.Queue;

Logger.Context = "Main";
Logger.Initialize();

Log.Information("Starting...");

string? machineId = Environment.GetEnvironmentVariable("RAILWAY_REPLICA_ID");
bool isDev = machineId is null;
if (isDev)
{
    Log.Warning("RAILWAY_REPLICA_ID not set, assuming dev environment");
}

string host = Environment.GetEnvironmentVariable($"REDIS_{(isDev ? "PUBLIC_" : "")}HOST") ?? throw new InvalidOperationException($"Environment variable 'REDIS_{(isDev ? "PUBLIC_" : "")}HOST' is not set");
string port = Environment.GetEnvironmentVariable($"REDIS_{(isDev ? "PUBLIC_" : "")}PORT") ?? throw new InvalidOperationException($"Environment variable 'REDIS_{(isDev ? "PUBLIC_" : "")}PORT' is not set");
string password = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? throw new InvalidOperationException("Environment variable 'REDIS_PASSWORD' is not set");

Postgres.Init();
Redis.Init();
SentryConnection.Init();
API.Init();

var client = new QueueClient($"{host}:{port},password={password},abortConnect=false");
Log.Information("Created queue client");

CancellationTokenSource cts = new();
void OnExit(string reason = "")
{
    if (cts.IsCancellationRequested) return;
    cts.Cancel();
    Log.Warning($"Stopping from {reason}...");
}

PosixSignalRegistration.Create(PosixSignal.SIGINT, (_) => { OnExit("SIGINT"); });
PosixSignalRegistration.Create(PosixSignal.SIGTERM, (_) => { OnExit("SIGTERM"); });
AppDomain.CurrentDomain.ProcessExit += (_,_) => { OnExit("SIGTERM"); };

string? workersEnv = Environment.GetEnvironmentVariable("WORKERS");
int workers = workersEnv is not null && int.TryParse(workersEnv, out int w) ? w : 5;
for (int i = 1; i <= workers; i++)
{
    Runner.Run(client, i, cts);
}

if (!isDev) QueueUpdates.Init(client, cts);

await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => {});
await Task.Delay(5000); // Wait for any remaining stuffs to finish
Log.Error("Goodbye!");
Logger.Shutdown();