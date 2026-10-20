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
using CounterStrikeSharp.API.Modules.Cvars;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

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

        [JsonPropertyName("NotifyBeforeRestart")]
        public int NotifyBeforeRestart { get; set; } = 60;

        [JsonPropertyName("AlertInterval")]
        public int AlertInterval { get; set; } = 10;

        [JsonPropertyName("WebhookEnabled")]
        public bool WebhookEnabled { get; set; } = false;

        [JsonPropertyName("WebhookUrl")]
        public string WebhookUrl { get; set; } = "";
    }

    public class AutoRestart : BasePlugin, IPluginConfig<AutoRestartConfig>
    {
        public override string ModuleName => "AutoRestart";
        public override string ModuleVersion => "1.0.1";
        public override string ModuleAuthor => "M1k@c";

        public required AutoRestartConfig Config { get; set; }
        private static IStringLocalizer? _Localizer;
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private bool _restartPending;
        private bool _manualRestartPending;
        private readonly List<CounterStrikeSharp.API.Modules.Timers.Timer> _restartTimers = new();

        private void AddRestartTimer(float delay, Action callback)
        {
            _restartTimers.Add(AddTimer(delay, callback));
        }

        public void OnConfigParsed(AutoRestartConfig config)
        {
            config.NotifyBeforeRestart = Math.Max(0, config.NotifyBeforeRestart);
            config.AlertInterval = Math.Max(1, config.AlertInterval);
            Config = config;
        }

        public override void Load(bool hotReload)
        {
            Console.WriteLine("[AutoRestart] Plugin loaded successfully!");

            _Localizer = Localizer;

            AddCommand("css_restart", "Restart the server after the configured countdown", (player, info) => RestartCommand(player));

            if (Config.AutoRestartEnabled)
            {
                ScheduleAutoRestart();
            }
        }

        private bool HasAdminPermission(CCSPlayerController? player)
        {
            return string.IsNullOrWhiteSpace(Config.Flag) || (player != null && AdminManager.PlayerHasPermissions(player, Config.Flag));
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

            BeginRestart(delay.TotalSeconds);
        }

        private void BeginRestart(double delaySeconds)
        {
            if (_restartPending)
                return;

            _restartPending = true;
            int warningSeconds = (int)Math.Min(Config.NotifyBeforeRestart, Math.Floor(delaySeconds));

            for (int remaining = warningSeconds; remaining > 0;)
            {
                int seconds = remaining;
                double alertDelay = delaySeconds - seconds;

                if (alertDelay <= 0)
                    NotifyPlayers(seconds);
                else
                    AddRestartTimer((float)alertDelay, () => NotifyPlayers(seconds));

                if (remaining <= Config.AlertInterval)
                    break;

                remaining -= Config.AlertInterval;
            }

            if (Config.WebhookEnabled && !string.IsNullOrWhiteSpace(Config.WebhookUrl))
            {
                if (delaySeconds <= 5)
                    SendRestartWebhook();
                else
                    AddRestartTimer((float)(delaySeconds - 5), SendRestartWebhook);
            }

            if (delaySeconds <= 0)
                RestartServer();
            else
                AddRestartTimer((float)delaySeconds, RestartServer);
        }

        private void NotifyPlayers(int seconds)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.IsValid && !player.IsBot && !player.IsHLTV)
                    player.PrintToChat(Localizer.ForPlayer(player, "restart_countdown", seconds));
            }
        }

        private void SendRestartWebhook()
        {
            string serverName = ConVar.Find("hostname")?.StringValue ?? "CS2 Server";
            string map = Server.MapName;
            int players = Utilities.GetPlayers().Count(player => player.IsValid && !player.IsBot && !player.IsHLTV);
            DateTimeOffset now = DateTimeOffset.Now;
            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = serverName,
                        color = 3066993,
                        timestamp = now.ToUniversalTime().ToString("O"),
                        fields = new[]
                        {
                            new { name = Localizer["webhook_map"].Value, value = map, inline = true },
                            new { name = Localizer["webhook_players"].Value, value = players.ToString(), inline = true },
                            new { name = Localizer["webhook_date"].Value, value = now.ToString("yyyy-MM-dd HH:mm:ss"), inline = false },
                            new { name = Localizer["webhook_log"].Value, value = Localizer["webhook_message"].Value, inline = false }
                        }
                    }
                }
            };
            _ = PostWebhookAsync(Config.WebhookUrl, payload);
        }

        private static async Task PostWebhookAsync(string url, object payload)
        {
            try
            {
                using var response = await Http.PostAsJsonAsync(url, payload);
                if (!response.IsSuccessStatusCode)
                    Console.WriteLine($"[AutoRestart] Restart webhook failed: HTTP {(int)response.StatusCode}.");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[AutoRestart] Restart webhook failed ({exception.GetType().Name}).");
            }
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
            
            if (_manualRestartPending)
                return;

            foreach (var timer in _restartTimers)
                timer.Kill();
            _restartTimers.Clear();
            _restartPending = false;
            _manualRestartPending = true;
            BeginRestart(Config.NotifyBeforeRestart);
        }
    }
}
