using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.Databases;

namespace Whispbot.PRC.Updates.Automod
{
    public class AutomodAction
    {
        public string Id { get; init; } = default!;
        [JsonProperty("rule_id")]
        public string RuleId { get; init; } = default!;
        public string Name { get; init; } = default!;
        public AutomodActionType Action { get; init; } = default!;
        [JsonProperty("execute_after_seconds")]
        public int ExecuteAfterSeconds { get; init; } = default!;
        public AutomodActionOptions Options { get; init; } = default!;

        public static List<AutomodAction>? FromRuleId(string ruleId)
        {
            return Postgres.Select<AutomodAction>("SELECT * FROM erlc_vehicle_automod_actions WHERE rule_id = @1", [ruleId]);
        }
    }

    public class AutomodActionOptions
    {
        public string? Message { get; init; } = default!;
        [JsonProperty("moderation_reason")]
        public string? ModerationReason { get; init; } = default!;
        [JsonProperty("moderation_type_id")]
        public string? ModerationTypeId { get; init; } = default!;
    }

    public enum AutomodActionType
    {
        None = 0,
        Message = 1,
        LogAndMessage = 2,
        Respawn = 3,
        RespawnAndMessage = 4,
        RespawnLogAndMessage = 5,
        Wanted = 6,
        WantedAndMessage = 7,
        WantedLogAndMessage = 8,
        Kick = 9,
        KickAndMessage = 10,
        Ban = 11,
        BanAndMessage = 12
    }
}
