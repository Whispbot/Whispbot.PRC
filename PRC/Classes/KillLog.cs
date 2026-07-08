using System;
using System.Collections.Generic;
using System.Text;

namespace Whispbot.PRC.PRC.Classes
{
    public class ERLCKillLog
    {
        public string Killer { get; init; } = default!;
        public string Killed { get; init; } = default!;
        public long Timestamp { get; init; } = default!;
    }
}
