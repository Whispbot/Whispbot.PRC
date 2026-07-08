using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using Whispbot.PRC.Databases;
using Whispbot.PRC.PRC.Classes;

namespace Whispbot.PRC.Updates
{
    public static class OnServer
    {
        public static void Handle(string serverId, ERLCServer server)
        {
            // Ok hear me out
            // this file is obsurdly long and complicated
            // if you have any better ideas on how to do this please speak up
            // also we will probably move to clickhouse in the future but for now this works

            try
            {
                Postgres.Execute(
                    @"
                        UPDATE erlc_servers SET
                            name = @2,
                            code = @3,
                            ingame_players = @4,
                            update_info = now() + @5::interval
                        WHERE internal_id = @1
                        RETURNING *;
                    ",
                    [serverId, server.Name, server.JoinKey, server.CurrentPlayers, Interval.ForServerUpdate(server)]
                );

                if (server.CurrentPlayers > 0)
                {
                    int ingame_staff = server.Players?.Count(p => p.Permission != "Normal") ?? 0;
                    Dictionary<string, int> teamCounts = server.Players?.GroupBy(p => p.Team).ToDictionary(g => g.Key, g => g.Count()) ?? [];
                    int ingame_civilian = teamCounts.GetValueOrDefault("Civilian");
                    int ingame_police = teamCounts.GetValueOrDefault("Police");
                    int ingame_sheriff = teamCounts.GetValueOrDefault("Sheriff");
                    int ingame_fire = teamCounts.GetValueOrDefault("Fire");
                    int ingame_dot = teamCounts.GetValueOrDefault("DOT");
                    int ingame_jailed = teamCounts.GetValueOrDefault("Jail");
                    int pending_mod_calls = server.ModCalls?.Count(mc => mc.Moderator is null) ?? 0;

                    Postgres.Execute(
                        @"
                        WITH updated_server AS (
                            SELECT id FROM erlc_servers WHERE internal_id = @1 LIMIT 1
                        )
                        INSERT INTO erlc_stats (
                            server_id, 
                            ingame_players, 
                            max_players, 
                            ingame_staff, 
                            ingame_civilian, 
                            ingame_police, 
                            ingame_sheriff, 
                            ingame_fire, 
                            ingame_dot, 
                            ingame_jailed, 
                            queued_players, 
                            pending_mod_calls, 
                            spawned_vehicles, 
                            total_coowners, 
                            total_admins, 
                            total_mods, 
                            total_helpers
                        ) VALUES (
                            (SELECT id FROM updated_server),
                            @2,
                            @3,
                            @4,
                            @5,
                            @6,
                            @7,
                            @8,
                            @9,
                            @10,
                            @11,
                            @12,
                            @13,
                            @14,
                            @15,
                            @16,
                            @17
                        )
                        ",
                        [
                            serverId,
                            server.CurrentPlayers,
                            server.MaxPlayers,
                            ingame_staff,
                            ingame_civilian,
                            ingame_police,
                            ingame_sheriff,
                            ingame_fire,
                            ingame_dot,
                            ingame_jailed,
                            server.Queue?.Count ?? 0,
                            pending_mod_calls,
                            server.Vehicles?.Count ?? 0,
                            server.CoOwnerIds.Count,
                            server.Staff?.Admins.Count ?? 0,
                            server.Staff?.Mods.Count ?? 0,
                            server.Staff?.Helpers.Count ?? 0
                        ]
                    );



                    List<object> args = [
                        serverId,
                        ..(server.Players?.SelectMany(p =>
                        {
                            var vehicle = server.Vehicles?.FirstOrDefault(v => v.Owner == p.Player.Split(':')[0]);

                            return new List<object> {
                                long.Parse(p.Player.Split(':')[1]),
                                p.Player.Split(':')[0],
                                p.Team,
                                p.WantedStars * 2,
                                p.Callsign ?? "",
                                p.Permission,
                                p.Location.LocationX,
                                p.Location.LocationZ,
                                p.Location.PostalCode,
                                p.Location.StreetName,
                                p.Location.BuildingNumber,
                                vehicle?.Name ?? "",
                                vehicle?.Texture ?? "",
                                vehicle?.ColorHex ?? "",
                                vehicle?.ColorName ?? "",
                                vehicle?.Plate ?? ""
                            };
                        }) ?? [])
                    ];

                    int idx = 2;
                    Postgres.Execute(
                        @$"
                            WITH server AS (
                                SELECT id FROM erlc_servers WHERE internal_id = @1 LIMIT 1
                            ),
                            current_players (
                                player_id,
                                player_name,
                                team,
                                wanted_stars,
                                callsign,
                                permission,
                                position_x,
                                position_y,
                                position_postal,
                                position_street,
                                position_building,
                                vehicle_name,
                                vehicle_texture,
                                vehicle_colour_hex,
                                vehicle_colour_name,
                                vehicle_plate
                            ) AS (
                                VALUES
                                {string.Join(",\n", server.Players?.Select(p => $"(@{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++}, @{idx++})") ?? [])}
                            )
                            INSERT INTO erlc_players (
                                server_id,
                                player_id,
                                player_name,
                                last_seen,
                                team,
                                wanted_stars,
                                callsign,
                                permission,
                                position_x,
                                position_y,
                                position_postal,
                                position_street,
                                position_building,
                                vehicle_name,
                                vehicle_texture,
                                vehicle_colour_hex,
                                vehicle_colour_name,
                                vehicle_plate
                            )
                            SELECT
                                (SELECT id FROM server),
                                cp.player_id,
                                cp.player_name,
                                now(),
                                cp.team,
                                cp.wanted_stars,
                                cp.callsign,
                                cp.permission,
                                cp.position_x,
                                cp.position_y,
                                cp.position_postal,
                                cp.position_street,
                                cp.position_building,
                                cp.vehicle_name,
                                cp.vehicle_texture,
                                cp.vehicle_colour_hex,
                                cp.vehicle_colour_name,
                                cp.vehicle_plate
                            FROM current_players AS cp
                            ON CONFLICT (server_id, player_id) DO UPDATE SET
                                player_name = EXCLUDED.player_name,
                                last_seen = EXCLUDED.last_seen,
                                team = EXCLUDED.team,
                                wanted_stars = EXCLUDED.wanted_stars,
                                callsign = EXCLUDED.callsign,
                                permission = EXCLUDED.permission,
                                position_x = EXCLUDED.position_x,
                                position_y = EXCLUDED.position_y,
                                position_postal = EXCLUDED.position_postal,
                                position_street = EXCLUDED.position_street,
                                position_building = EXCLUDED.position_building,
                                vehicle_name = EXCLUDED.vehicle_name,
                                vehicle_texture = EXCLUDED.vehicle_texture,
                                vehicle_colour_hex = EXCLUDED.vehicle_colour_hex,
                                vehicle_colour_name = EXCLUDED.vehicle_colour_name,
                                vehicle_plate = EXCLUDED.vehicle_plate;
                        ",
                        args
                    );

                    Postgres.Execute(
                        @"
                            DELETE FROM erlc_players 
                            WHERE server_id = (SELECT id FROM erlc_servers WHERE internal_id = @1 LIMIT 1)
                            AND (NOT (player_id = ANY(@2)) OR last_seen < now() - interval '2 minutes');
                        ",
                        [serverId, server.Players?.Select(p => long.Parse(p.Player.Split(':')[1])).ToArray() ?? []]
                    );
                }
                else
                {
                    Postgres.Execute(
                        @"DELETE FROM erlc_players WHERE server_id = (SELECT id FROM erlc_servers WHERE internal_id = @1 LIMIT 1);",
                        [serverId]
                    );
                }

                Logger.WithData(server).Debug($"Updated stats for {server.Name} ({serverId})");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update server stats:");
            }
        }
    }
}
