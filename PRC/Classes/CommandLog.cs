using System;
using System.Collections.Generic;
using System.Text;

namespace Whispbot.PRC.PRC.Classes
{
    public class ERLCCommandLog
    {
        public string Player { get; init; } = default!;
        public string Command { get; init; } = default!;
        public long Timestamp { get; init; } = default!;
    }
}
