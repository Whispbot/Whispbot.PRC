using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using YellowMacaroni.Redis.Queue;

namespace Whispbot.PRC.Messages
{
    public static class Reclaimer
    {
        /// <summary>
        /// Reclaims crashed entries from the queue and processes them using the Handler.
        /// </summary>
        /// <param name="queue"></param>
        /// <param name="cts"></param>
        public static void Start(RedisQueue<PRCRequest> queue, CancellationTokenSource cts)
        {
            var thread = new Thread(
                new ThreadStart(
                    async () =>
                    {
                        Log.Information("Reclaimer started");

                        while (!cts.IsCancellationRequested)
                        {
                            var reclaimed = await queue.ReclaimCrashed(30_000);

                            if (reclaimed.ClaimedEntries.Length != 0)
                            {
                                Log.Warning($"Reclaimed {reclaimed.ClaimedEntries.Length} entries");

                                foreach (var entry in reclaimed.ClaimedEntries)
                                {
                                    var message = queue.GetDataFromEntry(entry);
                                    if (message is null) continue;

                                    var (error, data, retryAfterMs) = await Handler.OnMessage(queue, entry, message);

                                    await queue.Return(entry, new PRCResponse
                                    {
                                        serverId = message.serverId,
                                        success = error == PRC.ErrorCode.Nothing,
                                        data = data
                                    });
                                }
                            }

                            await Task.Delay(10_000, cts.Token).ContinueWith(_ => { });
                        }

                        Log.Warning("Reclaimer finished");
                    }
                )
            )
            {
                Name = "Reclaimer"
            };

            thread.Start();
        }
    }
}
