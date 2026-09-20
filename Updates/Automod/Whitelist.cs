using Discord.Rest;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.Databases;
using Whispbot.PRC.Messages;
using Whispbot.PRC.PRC.Classes;

namespace Whispbot.PRC.Updates.Automod
{
    public static class Whitelist
    {
        private static DiscordRestClient? _client;
        private static DiscordRestClient Client
        {
            get
            {
                return _client ?? throw new Exception("Discord client not initialized");
            }
        }
        public static async Task Init()
        {
            string? token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
            if (string.IsNullOrEmpty(token))
            {
                throw new Exception("DISCORD_TOKEN environment variable is not set");
            }

            _client = new DiscordRestClient();

            _client.LoggedIn += async () => Log.Information($"Discord client logged in as {_client.CurrentUser.Username}");
            _client.Log += async (log) => Log.Verbose($"[{log.Source}][{log.Severity}] {log.Message}");

            await _client.LoginAsync(Discord.TokenType.Bot, token);
        }

        private static readonly Dictionary<(string, string), (RestGuildUser?, double)> _memberCache = [];

        private static async Task<RestGuildUser?> GetGuildUserAsync(PRCRequest request, ERLCPlayer player)
        {
            var guildId = request.DiscordServerId;
            var userId = player.DiscordId;

            return await Client.GetGuildUserAsync(guildId, userId);
        }

        private static bool HasIgnoredRole(RestGuildUser? member, AutomodRule rule)
        {
            return member is not null && member.RoleIds.Any(r => rule.IgnoredRoles.Contains(r));
        }

        public static async Task<bool> IsWhitelisted(PRCRequest request, AutomodRule rule, ERLCPlayer player)
        {
            if (rule.IgnoredRoles.Length == 0) return false;

            var cacheKey = (request.serverId!, player.Player);
            var cached = _memberCache.GetValueOrDefault(cacheKey, (null, 0));
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cached.Item2 < 300)
            {
                return HasIgnoredRole(cached.Item1, rule);
            }

            var member = await GetGuildUserAsync(request, player);
            _memberCache[cacheKey] = (member, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            return HasIgnoredRole(member, rule);
        }

        public static void CleanCache()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var keysToRemove = _memberCache.Where(kvp => now - kvp.Value.Item2 >= 300).Select(kvp => kvp.Key).ToList();
            foreach (var key in keysToRemove)
            {
                _memberCache.Remove(key);
            }
        }
    }
}
