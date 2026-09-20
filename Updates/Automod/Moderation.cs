using System;
using System.Collections.Generic;
using System.Text;

namespace Whispbot.PRC.Updates.Automod
{
    public class ModerationRequest
    {
        public ulong serverId;
        public ulong userId;
        public string type = null!;
        public string? reason;
    }
}
