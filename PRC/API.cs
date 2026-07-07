using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.Messages;

namespace Whispbot.PRC.PRC
{
    public static class API
    {
        public static readonly string? globalAPIKey = Environment.GetEnvironmentVariable("PRC_GLOBAL_KEY");
        public static readonly bool HasGlobalAPIKey = globalAPIKey is not null;

        private static readonly HttpClient _client = new()
        {
            BaseAddress = new("https://api.erlc.gg")
        };

        public static void Init()
        {
            if (HasGlobalAPIKey)
            {
                _client.DefaultRequestHeaders.Add("Authorization", globalAPIKey);
            }
        }

        private static HttpMethod GetMethodFromString(string method) => method.ToUpper() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            _ => throw new ArgumentException($"Invalid HTTP method: {method}")
        };

        public static async Task<HttpResponseMessage> Request(PRCRequest request)
        {
            var message = new HttpRequestMessage(GetMethodFromString(request.method), request.endpoint);

            if (request.serverId is not null && request.apiKey is not null)
            {
                message.Headers.Add("server-key", request.FullAPIKey);
            }

            if (request.method == "POST")
            {
                if (request.body is not null)
                {
                    var json = JsonConvert.SerializeObject(request.body);
                    message.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }
            }

            return await _client.SendAsync(message);
        }
    }
}
