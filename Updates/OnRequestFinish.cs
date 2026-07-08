using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.Messages;
using Whispbot.PRC.PRC;

namespace Whispbot.PRC.Updates
{
    public static class OnRequestFinish
    {
        public static void Handle(PRCRequest request, PRCResponse response)
        {
            if (
                request.serverId is not null
                && request.method == "GET" 
                && API.GetPath(request.endpoint) == "/v2/server"
            )
            {
                // A server has been fetched!!!!

                var server = response.Server;
                if (server is null) return;

                Task.Run(() => OnServer.Handle(request.serverId, server));
            }
        }
    }
}
