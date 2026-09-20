using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.Databases;

namespace Whispbot.PRC.PRC.Classes
{
    public class ERLCPlayer
    {
        public string Team { get; init; } = default!;
        public string Player { get; init; } = default!;
        public string? Callsign { get; init; }
        public string Permission { get; init; } = default!;
        public float WantedStars { get; init; } = default!;
        public ERLCPlayerLocation Location { get; init; } = default!;


        private ulong? _discordId;
        public ulong DiscordId
        {
            get
            {
                if (_discordId is not null) return _discordId.Value;

                var parts = Player.Split(":");
                var robloxIdStr = parts.Length > 1 ? parts[1] : null;
                if (!ulong.TryParse(robloxIdStr, out var id)) throw new InvalidOperationException("Failed to parse Roblox ID");

                var userConfig = Postgres.SelectFirst<GetUserId>("SELECT id FROM user_config WHERE roblox_id = @1;", [id])
                    ?? throw new InvalidOperationException("Failed to find user config");

                _discordId = userConfig.id;
                return _discordId.Value;
            }
        }
        private class GetUserId
        {
            public ulong id;
        }
    }

    public class ERLCPlayerLocation
    {
        public float LocationX { get; init; } = -1;
        public float LocationZ { get; init; } = -1;
        public string PostalCode { get; init; } = default!;
        public string StreetName { get; init; } = default!;
        public string BuildingNumber { get; init; } = default!;
    }
}
