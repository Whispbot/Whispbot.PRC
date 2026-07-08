using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.Databases;
using Whispbot.PRC.Databases;
using Whispbot.PRC.Messages;
using Whispbot.PRC.PRC;

namespace Whispbot.PRC.Updates
{
    public static class BadAPIKeys
    {
        private static void InvalidateKeys(string serverId)
        {
            Postgres.Execute("UPDATE erlc_servers SET api_key = NULL WHERE id = @1", [serverId]);
        }

        public static readonly Dictionary<ErrorCode, int> InvalidKeyCodes = new()
        {
            { ErrorCode.ServerKeyMalformed, 50 }, // Malformed key, allow retry incase of a temporary issue
            { ErrorCode.ServerKeyInvalid, 50 }, // Reset key?? Allow retry incase of a temporary issue
            { ErrorCode.ServerKeyBanned, 100 }, // Nope
            { ErrorCode.NotAuthorized, 10 }, // Stop sending commands and authorize the app bruh
            { ErrorCode.MessageProhibited, 40 } // Said bad bad thing, 3 strikes and ur out
        };

        public static void CheckForBadKeys(PRCResponse response)
        {
            if (response.serverId is null) return;

            if (InvalidKeyCodes.TryGetValue(response.error, out var score))
            {
                var redis = Redis.GetDatabase();
                if (redis is null) return;

                string key = $"prc:bad_server:{response.serverId}";

#pragma warning disable SER006 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                long newScore = redis.StringIncrement(key, score, TimeSpan.FromMinutes(5)).Value;
#pragma warning restore SER006 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

                if (newScore >= 100)
                {
                    InvalidateKeys(response.serverId);
                    redis.KeyExpire(key, TimeSpan.FromHours(60)); // Temp ban
                }
            }
        }
    }
}
