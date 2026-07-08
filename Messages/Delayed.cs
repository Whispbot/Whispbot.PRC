using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using YellowMacaroni.Redis.Queue;

namespace Whispbot.PRC.Messages
{
    public static class Delayed
    {
        public static void StartReclaimer(RedisQueue<PRCRequest> queue, CancellationTokenSource cts)
        {
            var thread = new Thread(
                new ThreadStart(
                    async () =>
                    {
                        Logger.Context = Thread.CurrentThread.Name!;
                        Log.Information("Delay reclaimer started");

                        while (!cts.IsCancellationRequested)
                        {
                            var reclaimed = await queue.RequeueDelayedJobs();

                            if (reclaimed > 0)
                            {
                                Log.Debug($"Reclaimed {reclaimed} delayed jobs");
                            }

                            await Task.Delay(500, cts.Token).ContinueWith(_ => { });
                        }

                        Log.Warning("Delay reclaimer finished");
                    }
                )
            )
            {
                Name = "Delays",
                IsBackground = true
            };

            thread.Start();
        }
    }
}
