using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.PRC;

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
