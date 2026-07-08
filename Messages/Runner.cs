using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.O11y;
using Whispbot.PRC.PRC;
using Whispbot.PRC.Updates;
using YellowMacaroni.Redis.Queue;

namespace Whispbot.PRC.Messages
{
    public static class Runner
    {
        public static readonly List<ErrorCode> retryErrors = [
            ErrorCode.CircuitBreakerOpen,
            ErrorCode.RobloxServerError,
            ErrorCode.Ratelimited
        ];

        public static void Run(QueueClient client, int threadId, CancellationTokenSource cts)
        {
            var thread = new Thread(
                new ThreadStart(
                    async () =>
                    {
                        Logger.Context = Thread.CurrentThread.Name!;

                        string? replicaId = Environment.GetEnvironmentVariable("RAILWAY_REPLICA_ID");
                        string machineId = $"{replicaId ?? "local"}-{threadId}";
                        Log.Information($"Starting worker {threadId}");

                        var queue = new RedisQueue<PRCRequest>(
                            client,
                            $"prc_api{(replicaId is null ? "_dev" : "")}",
                            new QueueOptions
                            {
                                GroupName = "prc_api_workers",
                                MachineId = machineId,
                                PublishEvents = true,
                                DequeueCount = 1,
                                QueuePositionToRetrieveFrom = QueueOptions.QueuePosition.NotClaimed
                            }
                        );
                        Log.Information($"Joined queue as {machineId}");

                        if (threadId == 1) // Primary thread
                        {
                            Reclaimer.Start(queue, cts);
                            Delayed.StartReclaimer(queue, cts);
                        }

                        await queue.ListenForMessagesWithCallback(
                            async (entry) =>
                            {
                                var message = queue.GetDataFromEntry(entry);
                                if (message is null) return QueueResponse<PRCResponse>.Retry();

                                var cached = await Cache.GetCache(message);
                                if (cached is not null) {
                                    entry.RecordEnd(message, cached);
                                    return QueueResponse<PRCResponse>.Success(cached);
                                }

                                var (error, data, retryAfterMs) = await Handler.OnMessage(queue, entry, message);

                                if (retryErrors.Contains(error) && entry.GetAttempt() < 3) return QueueResponse<PRCResponse>.Retry(retryAfterMs);

                                var response = new PRCResponse
                                {
                                    serverId = message.serverId,
                                    success = error == ErrorCode.Nothing,
                                    error = error,
                                    error_message = Errors.GetErrorMessage(error),
                                    data = data
                                };

                                await Cache.SetCache(message, response);

                                entry.RecordEnd(message, response);

                                OnRequestFinish.Handle(message, response);

                                return QueueResponse<PRCResponse>.Success(response);
                            },
                            cts.Token
                        );

                        Log.Warning($"Worker {threadId} finished");
                    }
                )
            )
            {
                Name = $"Worker {threadId}"
            };

            thread.Start();
        }
    }
}
