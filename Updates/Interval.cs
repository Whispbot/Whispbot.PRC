using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.PRC.Classes;

namespace Whispbot.PRC.Updates
{
    public static class Interval
    {
        public static readonly int maxPlayers = 50;

        public static readonly TimeSpan maxInterval = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan minInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Generates a string interval for the next server update based on the current server state.
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        public static string ForServerUpdate(ERLCServer server)
        {
            if (server.CurrentPlayers == 0)
            {
                // Server is currently empty, so we don't need to update it frequently
                return "2 minutes";
            }

            double playerRatio = (double)server.CurrentPlayers / (double)server.MaxPlayers;
            TimeSpan duration = maxInterval - minInterval;
            TimeSpan interval = minInterval + TimeSpan.FromSeconds(duration.TotalSeconds * (1 - playerRatio));

            // Use the currently ingame players to calculate the interval so that
            // more busy servers get updated more frequently than less busy servers.

            // To be updated in the future with more parameters such as server activity,
            // whether someone is viewing the live map, etc.
            return $"{Math.Ceiling(interval.TotalSeconds)} seconds";
        }
    }
}
