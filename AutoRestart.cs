using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Config;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;

namespace AutoRestart
{
    public class AutoRestartConfig : BasePluginConfig
    {
        [JsonPropertyName("AutoRestartEnabled")]
        public bool AutoRestartEnabled { get; set; } = true;

        [JsonPropertyName("EnableManualRestart")]
        public bool EnableManualRestart { get; set; } = true;

        [JsonPropertyName("Flag")]
        public string Flag { get; set; } = "@css/root";

        [JsonPropertyName("AutoRestartTime")]
        public string AutoRestartTime { get; set; } = "01:00:00";
    }

    public class AutoRestart : BasePlugin, IPluginConfig<AutoRestartConfig>
    {
        public override string ModuleName => "AutoRestart";
        public override string ModuleVersion => "1.0.0";
        public override string ModuleAuthor => "M1k@c";

        public required AutoRestartConfig Config { get; set; }
        private static IStringLocalizer? _Localizer;

        public void OnConfigParsed(AutoRestartConfig config)
        {
            Config = config;
        }

        public override void Load(bool hotReload)
        {
            Console.WriteLine("[AutoRestart] Plugin loaded successfully!");

            _Localizer = Localizer;

            AddCommand("css_restart", "Restart the server immediately", (player, info) => RestartCommand(player));

            if (Config.AutoRestartEnabled)
            {
                ScheduleAutoRestart();
            }
        }

        private bool HasAdminPermission(CCSPlayerController? player)
        {
            return player != null && AdminManager.PlayerHasPermissions(player, Config.Flag);
        }

        private void ScheduleAutoRestart()
        {
            if (!TimeSpan.TryParse(Config.AutoRestartTime, out TimeSpan restartTime))
            {
                Console.WriteLine($"[AutoRestart] Invalid AutoRestartTime format: {Config.AutoRestartTime}. Expected format: HH:mm:ss");
                return;
            }

            TimeSpan currentTime = DateTime.Now.TimeOfDay;
            TimeSpan delay = restartTime > currentTime
                ? restartTime - currentTime
                : restartTime.Add(TimeSpan.FromDays(1)) - currentTime;

            Console.WriteLine($"[AutoRestart] Server will restart at {restartTime} ({delay.TotalSeconds} seconds from now).");

            AddTimer((float)delay.TotalSeconds, RestartServer);
        }

        private void RestartServer()
        {
            Console.WriteLine("[AutoRestart] Restarting server via command...");
            Server.ExecuteCommand("quit");
        }

        private void RestartCommand(CCSPlayerController? player)
        {
            if (!HasAdminPermission(player))
            {
                Console.WriteLine("[AutoRestart] Player has no admin permission for restart.");
                return;
            }

            if (!Config.EnableManualRestart)
            {
                Server.NextFrame(() =>
                {
                    if (player?.IsValid == true)
                        player.PrintToChat(_Localizer?.ForPlayer(player, "manual_restart_disabled") ?? "Manual restart is disabled.");
                });
                return;
            }

            Console.WriteLine("[AutoRestart] Admin executed 'css_restart' command.");
            Server.NextFrame(() =>
            {
                if (player?.IsValid == true)
                    player.PrintToChat(_Localizer?.ForPlayer(player, "restarting_now") ?? "Restarting server now...");
            });
            RestartServer();
        }
    }
}