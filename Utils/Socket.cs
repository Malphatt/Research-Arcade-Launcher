using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArcademiaGameLauncher.Services;
using ArcademiaGameLauncher.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArcademiaGameLauncher.Utils
{
    internal class Socket
    {
        private readonly MainWindow _mainWindow;
        private readonly ISfxPlayer _sfxPlayer;
        private readonly HubConnection _hub;
        private readonly CancellationTokenSource _heartbeatCts = new();
        private readonly ILogger<Socket> _logger;

        private int _machineId;
        private int _siteId;
        private string _machineName = "Unknown";

        public Socket(
            string baseUrl,
            string authUser,
            string authPass,
            MainWindow mainWindow,
            ISfxPlayer sfxPlayer,
            ILogger<Socket> logger
        )
        {
            _mainWindow = mainWindow;
            _sfxPlayer = sfxPlayer;
            _logger = logger;

            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{authUser}:{authPass}"));

            var hubUrl = $"{baseUrl.TrimEnd('/')}/ws/machine";
            _logger.LogInformation("[SignalR] Connecting to {HubUrl}", hubUrl);

            _hub = new HubConnectionBuilder()
                .AddJsonProtocol(o =>
                {
                    o.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                })
                .WithUrl(
                    hubUrl,
                    options =>
                    {
                        options.Headers.Add("Authorization", $"ArcadeMachine {creds}");
                    }
                )
                .WithAutomaticReconnect()
                .Build();

            WireServerEvents();
            _ = ConnectAndStartHeartbeat();
        }

        private async Task ConnectAndStartHeartbeat()
        {
            var ct = _heartbeatCts.Token;
            int delayMs = 2000;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _hub.StartAsync(ct);
                    _logger.LogInformation("[SignalR] Connected");

                    await SafeInvokeAck("Client connected");
                    await SafeReportStatus("Idle");

                    // Start a single heartbeat loop tied to the same token
                    _ = Task.Run(
                        async () =>
                        {
                            while (!ct.IsCancellationRequested)
                            {
                                try
                                {
                                    if (_hub.State == HubConnectionState.Connected)
                                    {
                                        await _hub.InvokeAsync("Heartbeat", cancellationToken: ct);
                                        _logger.LogDebug("[SignalR] Heartbeat sent");
                                    }
                                    else
                                    {
                                        _logger.LogDebug(
                                            "[SignalR] Skipping heartbeat (state: {State})",
                                            _hub.State
                                        );
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "[SignalR] Heartbeat failed");
                                }

                                try
                                {
                                    await Task.Delay(TimeSpan.FromSeconds(60), ct);
                                }
                                catch { }
                            }
                        },
                        ct
                    );

                    // once connected, exit the initial connect loop
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "[SignalR] Initial connect failed – will retry in {DelayMs} ms",
                        delayMs
                    );
                    try
                    {
                        await Task.Delay(delayMs, ct);
                    }
                    catch { }
                    delayMs = Math.Min(delayMs * 2, 60_000);
                }
            }
        }

        private void WireServerEvents()
        {
            _hub.Closed += async (ex) =>
            {
                _logger.LogWarning("[SignalR] Disconnected: {Error}", ex?.Message);

                var ct = _heartbeatCts.Token;
                int delayMs = 2000;

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(delayMs, ct);
                        await _hub.StartAsync(ct);
                        _logger.LogInformation("[SignalR] Reconnected after close");
                        break;
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(
                            e,
                            "[SignalR] Reconnect attempt failed – will retry in {DelayMs} ms",
                            delayMs
                        );
                        delayMs = Math.Min(delayMs * 2, 60_000);
                    }
                }
            };

            _hub.Reconnected += (id) =>
            {
                _logger.LogInformation("[SignalR] Reconnected (ConnId: {Id})", id);
                return Task.CompletedTask;
            };

            _hub.Reconnecting += (ex) =>
            {
                _logger.LogWarning("[SignalR] Reconnecting due to error: {Error}", ex?.Message);
                return Task.CompletedTask;
            };

            // Server To Client Events
            _hub.On(
                "UpdateUpdater",
                async () =>
                {
                    _logger.LogInformation("[SignalR] Received UpdateUpdater");
                    await _mainWindow.CheckForUpdaterUpdates();
                }
            );

            _hub.On(
                "UpdateLauncher",
                () =>
                {
                    _logger.LogInformation("[SignalR] Received UpdateLauncher");
                    _mainWindow.RestartLauncher();
                }
            );

            _hub.On(
                "UpdateGames",
                async () =>
                {
                    _logger.LogInformation("[SignalR] Received UpdateGames");
                    await _mainWindow.CheckForGameDatabaseChanges();
                }
            );

            // PlaySFX(string fileUrl)
            _hub.On<string>(
                "PlaySFX",
                async (fileUrl) =>
                {
                    _logger.LogInformation("[SignalR] Received PlaySFX: {FileUrl}", fileUrl);
                    await _sfxPlayer.PlayAsync(fileUrl);
                }
            );

            _hub.On<RegisteredPayload>(
                "Registered",
                payload =>
                {
                    _machineId = payload.MachineId;
                    _siteId = payload.SiteId;
                    _machineName = payload.MachineName ?? "Unknown";

                    _logger.LogInformation(
                        "[SignalR] Registered as '{MachineName}' (ID: {MachineId}, Site: {SiteId})",
                        _machineName,
                        _machineId,
                        _siteId
                    );
                }
            );
        }

        private async Task SafeInvokeAck(string message)
        {
            try
            {
                await _hub.InvokeAsync("Ack", message);
                _logger.LogDebug("[SignalR] Ack sent: {Message}", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SignalR] Failed to send Ack");
            }
        }

        public async Task StopAsync()
        {
            _heartbeatCts.Cancel();
            try
            {
                await _hub.StopAsync();
            }
            catch { }
            _hub.DisposeAsync().AsTask().Wait(1000);
            _logger.LogInformation("[SignalR] Connection stopped");
        }

#nullable enable
        public async Task SafeReportStatus(string status, string? ext = null)
#nullable restore
        {
            _logger.LogInformation("[SignalR] Reporting status: {Status} {Ext}", status, ext ?? "");

            try
            {
                await _hub.InvokeAsync("ReportStatus", status, ext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SignalR] ReportStatus failed");
            }
        }
    }

    public sealed class RegisteredPayload
    {
        public int MachineId { get; set; }
        public string MachineName { get; set; } = "Unknown";
        public int SiteId { get; set; }
        public DateTime ServerTimeUtc { get; set; }
    }
}
