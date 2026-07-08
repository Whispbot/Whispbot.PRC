using Serilog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.Databases;
using Whispbot.PRC.Databases;
using Whispbot.PRC.Messages;
using YellowMacaroni.Redis.Queue;

namespace Whispbot.PRC.Updates
{
    public static class QueueUpdates
    {
        public static void Init(QueueClient client, CancellationTokenSource cts)
        {
            var thread = new Thread(
                new ThreadStart(
                    async () =>
                    {
                        Logger.Context = Thread.CurrentThread.Name!;
                        string? replicaId = Environment.GetEnvironmentVariable("RAILWAY_REPLICA_ID");
                        string machineId = $"{replicaId ?? "local"}-updates";

                        var queue = new RedisQueue<PRCRequest>(
                            client,
                            $"prc_api{(replicaId is null ? "_dev" : "")}",
                            new QueueOptions
                            {
                                GroupName = "prc_api_updates",
                                MachineId = machineId
                            }
                        );

                        Log.Information("Updater started");

                        var db = Redis.GetDatabase();

                        // only run when on prod (replicaId is not null)
                        var acquired = db is not null && await db.StringSetAsync(
                            "throttle:prc_randomize_query",
                            "1",
                            TimeSpan.FromMinutes(2),
                            When.NotExists
                        );

                        if (acquired)
                        {
                            // Randomize the update_info for all servers
                            // just incase the service has been offline
                            // and there are many servers that need to be
                            // updated. This will help stagger the updates
                            // so that we don't get overwhelmed or hit
                            // ratelimits.

                            // However, this is throttled using redis
                            // so that when multiple replicas are
                            // starting at the same time or the program
                            // is constantly crashing & restarting, we
                            // don't keep randomizing the update_info.
                            Postgres.Execute(@"
                                UPDATE erlc_servers
                                SET update_info = now() + (random() * interval '2 minutes')
                                WHERE api_key IS NOT NULL;
                            ");
                        }
                        

                        while (!cts.IsCancellationRequested)
                        {
                            Thread.Sleep(1000);

                            // Select servers that need to be updated but also
                            // update the update_info so that when there are
                            // multiple replicas, they don't all try to update
                            // the same server at the same time.
                            var needsUpdates = Postgres.Select<ERLCServerConfig>(
                                @"
                                    WITH servers AS MATERIALIZED (
                                        SELECT id FROM erlc_servers
                                        WHERE api_key IS NOT NULL 
                                            AND internal_id IS NOT NULL 
                                            AND update_info < NOW()
                                        FOR NO KEY UPDATE SKIP LOCKED
                                        LIMIT 10
                                    )
                                    UPDATE erlc_servers AS upd
                                    SET update_info = NOW() + INTERVAL '2 minutes'
                                    FROM servers
                                    WHERE upd.id = servers.id
                                    RETURNING upd.*;
                                "
                            );

                            if ((needsUpdates?.Count ?? 0) > 0)
                            {
                                Log.Debug($"Updating {needsUpdates!.Count} servers that need updates");

                                foreach (var server in needsUpdates)
                                {
                                    await queue.Enqueue(
                                        new PRCRequest
                                        {
                                            endpoint = "/v2/server?Players=true&Staff=true&JoinLogs=true&Queue=true&KillLogs=true&CommandLogs=true&ModCalls=true&EmergencyCalls=true&Vehicles=true",
                                            method = "GET",
                                            serverId = server.internal_id,
                                            apiKey = server.api_key
                                        }
                                    );
                                }
                            }
                        }
                    }
                )
            )
            {
                Name = "Updates",
                IsBackground = true
            };

            thread.Start();
        }
    }

    public class ERLCServerConfig
    {
        public Guid id;
        public long guild_id = 0;
        public bool is_default = false;
        public string api_key = "";
        public string internal_id = "";
        public int ingame_players = 0;
        public string? name = null;
        public string? code = null;

        public bool allow_ban_requests = true;
    }
}
