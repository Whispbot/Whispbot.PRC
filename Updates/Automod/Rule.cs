using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.Databases;

namespace Whispbot.PRC.Updates.Automod
{
    public class AutomodRule
    {
        public string Id { get; init; } = default!;
        [JsonProperty("server_id")]
        public string ServerId { get; init; } = default!;
        public string Name { get; init; } = default!;
        public bool Enabled { get; init; } = default!;
        public int Priority { get; init; } = default!;
        [JsonProperty("vehicle_filters")]
        public string[] VehicleFilters { get; init; } = default!;
        [JsonProperty("texture_filters")]
        public string[] TextureFilters { get; init; } = default!;
        [JsonProperty("ignored_roles")]
        public ulong[] IgnoredRoles { get; init; } = default!;

        public static List<AutomodRule>? FromServerId(string serverId)
        {
            return Postgres.Select<AutomodRule>("SELECT * FROM erlc_vehicle_automod_rules WHERE server_id = @1", [serverId]);
        }
    }

    public class AutomodRuleWithActions: AutomodRule
    {
        public List<AutomodAction> Actions { get; init; } = [];

        public new static List<AutomodRuleWithActions>? FromServerId(string serverId)
        {
            return Postgres.Select<AutomodRuleWithActions>(
                @"WITH server AS (
                    SELECT id
                    FROM erlc_servers
                    WHERE internal_id = @1
                )
                SELECT
                    r.id,
                    r.server_id,
                    r.name,
                    r.enabled,
                    r.priority,
                    r.vehicle_filters,
                    r.texture_filters,
                    r.ignored_roles,
                    COALESCE(
                        jsonb_agg(a) FILTER (WHERE a.id IS NOT NULL),
                        '[]'::jsonb
                    ) AS actions
                FROM erlc_vehicle_automod_rules AS r
                LEFT JOIN erlc_vehicle_automod_actions AS a
                    ON a.rule_id = r.id
                WHERE r.server_id = (SELECT id FROM server)
                  AND r.enabled = TRUE
                GROUP BY r.id;",
                [serverId]
            );
        }
    }
}
