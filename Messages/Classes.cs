using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.PRC;
using Whispbot.PRC.PRC.Classes;

namespace Whispbot.PRC.Messages
{
    public class PRCRequest
    {
        public string? serverId = null;
        public string? apiKey = null;
        public string endpoint = null!;
        public string method = null!;
        public object? body = null;

        private string? _decryptedApiKey = null;
        public string? DecryptedAPIKey
        {
            get
            {
                if (_decryptedApiKey is null && apiKey is not null)
                {
                    _decryptedApiKey = Encryption.DecryptApiKey(apiKey);
                }
                return _decryptedApiKey;
            }
        }
        public string? FullAPIKey => DecryptedAPIKey is not null && serverId is not null ? $"{DecryptedAPIKey}-{serverId}" : null;
    }

    public class PRCResponse
    {
        public string? serverId = null!;
        public bool success = false;
        public long cachedAtMs = -1;
        public ErrorCode error = ErrorCode.Nothing;
        public string? error_message = "Something went wrong...";
        public object? data = null!;

        public ERLCServer? Server => ConvertResponseTo<ERLCServer>(this);

        public static T? ConvertResponseTo<T>(PRCResponse response) where T : class
        {
            var data = response.data;
            if (data is null) return null;

            if (data is T t) return t;

            if (data is JToken token)
            {
                try
                {
                    return token.ToObject<T>();
                }
                catch
                {
                    return null;
                }
            }

            if (data is string s)
            {
                try
                {
                    return JsonConvert.DeserializeObject<T>(s);
                }
                catch
                {
                    return null;
                }
            }

            try
            {
                var serialized = JsonConvert.SerializeObject(data);
                return JsonConvert.DeserializeObject<T>(serialized);
            }
            catch
            {
                return null;
            }
        }
    }

    public class PRCError
    {
        public ErrorCode code;
        public string message = null!;
        [JsonProperty("learn-more-and-docs")]
        public string learn_more_and_docs = null!;
        [JsonProperty("api-dashboard")]
        public string api_dashboard = null!;
    }
}
