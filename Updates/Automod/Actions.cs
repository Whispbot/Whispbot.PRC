using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.Messages;
using Whispbot.PRC.PRC.Classes;
using YellowMacaroni.Redis.Queue;

namespace Whispbot.PRC.Updates.Automod
{
    public static class Actions
    {
        private static RedisQueue<PRCRequest>? _commandQueue = null;
        private static RedisQueue<PRCRequest> CommandQueue
        {
            get
            {
                return _commandQueue ?? throw new Exception("Queue not initialized");
            }
        }

        private static RedisQueue<ModerationRequest>? _moderationQueue = null;
        private static RedisQueue<ModerationRequest> ModerationQueue
        {
            get
            {
                return _moderationQueue ?? throw new Exception("Queue not initialized");
            }
        }

        public static void Init(QueueClient client)
        {
            string? replicaId = Environment.GetEnvironmentVariable("RAILWAY_REPLICA_ID");
            string machineId = $"{replicaId ?? "local"}-automod";

            _commandQueue = new RedisQueue<PRCRequest>(
                client,
                $"prc_api{(replicaId is null ? "_dev" : "")}",
                new QueueOptions
                {
                    GroupName = "prc_api_automod",
                    MachineId = machineId
                }
            );

            _moderationQueue = new RedisQueue<ModerationRequest>(
                client,
                $"moderations{(replicaId is null ? "_dev" : "")}",
                new QueueOptions
                {
                    GroupName = "prc_api_automod",
                    MachineId = machineId
                }
            );
        }

        public static async Task ExecuteActionAsync(PRCRequest request, AutomodAction action, ERLCVehicle vehicle, ERLCPlayer player)
        {
            switch (action.Action)
            {
                case AutomodActionType.Message:
                    await ExecuteMessageAsync(request, action, vehicle);
                    break;
            }
        }

        private static async Task ExecuteMessageAsync(PRCRequest request, AutomodAction action, ERLCVehicle vehicle)
        {
            if (action.Options.Message is null) return;
            SendCommand(request, $":pm {vehicle.Owner} {action.Options.Message}");
        }

        private static void SendCommand(PRCRequest request, string command)
        {
            CommandQueue.Enqueue(new PRCRequest
            {
                endpoint = "/v2/server/command",
                serverId = request.serverId,
                apiKey = request.apiKey,
                method = "POST",
                body = new
                {
                    command
                }
            });
        }

        private static void SendModeration(ModerationRequest request) => ModerationQueue.Enqueue(request);
        private static void SendModeration(PRCRequest request, ERLCPlayer player, AutomodAction action, string reason) => SendModeration(new ModerationRequest
        {
            serverId = request.DiscordServerId,
            userId = player.DiscordId,
            reason = reason,
            type = action.Options.ModerationTypeId ?? throw new Exception("ModerationTypeId is null")
        });
    }
}
