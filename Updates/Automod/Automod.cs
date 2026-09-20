using Serilog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Whispbot.Databases;
using Whispbot.PRC.Messages;
using Whispbot.PRC.PRC.Classes;
using YellowMacaroni.Redis.Queue;

namespace Whispbot.PRC.Updates.Automod
{
    public static class Automoderator
    {
        private static IDatabase? _database = null;
        private static IDatabase? Database
        {
            get
            {
                _database ??= Redis.GetDatabase();
                return _database;
            }
        }

        private static readonly string[] _ignoredRoles = [
            "Server Owner",
            "Server Co-Owner",
            "Server Administrator",
            "Server Moderator"
        ];

        public static async Task Run(PRCRequest request, ERLCServer server)
        {
            // Nothing to automod if there are no vehicles
            if ((server.Vehicles?.Count ?? 0) == 0) return;
            var database = Database;
            if (database is null) return;

            var serverId = request.serverId!;
            var rules = AutomodRuleWithActions.FromServerId(serverId);
            if (rules is null || rules.Count == 0) return;

            var orderedRules = rules.OrderBy(r => r.Id);

            foreach (var vehicle in server.Vehicles!)
            {
                // Skip if the vehicle owner is a mod+ (most moderative actions are ineffective and wastes requests)
                var player = server.Players?.Find(p => p.Player.StartsWith($"{vehicle.Owner}:"));
                if (player is null || _ignoredRoles.Contains(player.Permission)) continue;

                foreach (var rule in orderedRules)
                {
                    var matchesVehicle = 
                        rule.VehicleFilters.Length == 0 || 
                        rule.VehicleFilters.Any(f => MatchFilter(f, vehicle.Name));
                    var matchesTexture = 
                        matchesVehicle && 
                        vehicle.Texture is not null &&
                        (rule.TextureFilters.Length == 0 ||
                        rule.TextureFilters.Any(f => MatchFilter(f, vehicle.Texture)));

                    if (matchesTexture)
                    {
                        if (await Whitelist.IsWhitelisted(request, rule, player)) continue;

                        var key = GenerateActionKey(serverId, rule.Id, vehicle);
                        var latestActionTaken = await database.StringGetAsync(key);
                        var latestActionTTL = await database.KeyTimeToLiveAsync(key);

                        var maxDuration = TimeSpan.FromSeconds(rule.Actions.Max(r => r.ExecuteAfterSeconds));
                        var ruleResetTTL = maxDuration + TimeSpan.FromMinutes(2);
                        var duration = (ruleResetTTL - (latestActionTTL ?? ruleResetTTL));

                        Log.Debug($"{vehicle.Owner} violated rule {rule.Name} in {server.Name}");

                        string latestActionId = null!;
                        foreach (var action in rule.Actions.OrderBy(a => a.ExecuteAfterSeconds).SkipWhile(a => !latestActionTaken.IsNullOrEmpty && a.Id != latestActionTaken))
                        {
                            if (action.Id == latestActionTaken) continue;
                            if (action.ExecuteAfterSeconds > duration.TotalSeconds) break;

                            await Actions.ExecuteActionAsync(request, action, vehicle, player);

                            latestActionId = action.Id;
                        }
                        
                        if (rule.Actions.FindIndex(a => a.Id == latestActionId) == rule.Actions.Count - 1)
                        {
                            if (!latestActionTaken.IsNullOrEmpty)
                            {
                                // This is the last action, reset the key to allow for future actions
                                await database.KeyDeleteAsync(key);
                            }
                        }
                        else if (latestActionId != latestActionTaken)
                        {
                            await database.StringSetAsync(key, latestActionId, ruleResetTTL);
                        }

                        break; // Stop checking rules after the first match
                    }
                }
            }
        }

        private static bool MatchFilter(string filter, string value)
        {
            filter = filter.Replace("*", ".*");
            return Regex.IsMatch(value, filter, RegexOptions.IgnoreCase);
        }

        private static string GenerateActionKey(string serverId, string actionId, ERLCVehicle vehicle)
        {
            return $"vehicle_automod:{serverId}:{actionId}:{vehicle.Name}:{vehicle.Texture ?? "null"}";
        }
    }
}
