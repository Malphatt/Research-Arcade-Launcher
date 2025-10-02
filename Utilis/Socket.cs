using ArcademiaGameLauncher.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArcademiaGameLauncher.Utilis
{
    internal class Socket
    {
        private readonly MainWindow _mainWindow;
        private readonly HubConnection _hub;
        private readonly CancellationTokenSource _heartbeatCts = new();
        private readonly ILogger<Socket> _logger;

        private int _machineId;
        private int _siteId;
        private string _machineName = "Unknown";

        public Socket(string baseUrl, string authUser, string authPass, MainWindow mainWindow, ILogger<Socket> logger)
        {
            _mainWindow = mainWindow;
            _logger = logger;

            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{authUser}:{authPass}"));

            var hubUrl = $"{baseUrl.TrimEnd('/')}/ws/machine";
            _logger.LogInformation("[SignalR] Connecting to {HubUrl}", hubUrl);

            _hub = new HubConnectionBuilder()
                .AddJsonProtocol(o =>
                {
                    o.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                })
                .WithUrl(hubUrl, options =>
                {
                    options.Headers.Add("Authorization", $"ArcadeMachine {creds}");
                })
                .WithAutomaticReconnect()
                .Build();

            WireServerEvents();
            _ = ConnectAndStartHeartbeat();
        }

        private async Task ConnectAndStartHeartbeat()
        {
            try
            {
                await _hub.StartAsync();
                _logger.LogInformation("[SignalR] Connected");

                await SafeInvokeAck("Client connected");
                await SafeReportStatus("Idle");

                _ = Task.Run(async () =>
                {
                    while (!_heartbeatCts.IsCancellationRequested)
                    {
                        try
                        {
                            await _hub.InvokeAsync("Heartbeat");
                            _logger.LogDebug("[SignalR] Heartbeat sent");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[SignalR] Heartbeat failed");
                        }

                        await Task.Delay(TimeSpan.FromSeconds(60), _heartbeatCts.Token);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SignalR] Initial connect failed");
            }
        }

        private void WireServerEvents()
        {
            _hub.Closed += async (ex) =>
            {
                _logger.LogWarning("[SignalR] Disconnected: {Error}", ex?.Message);
                await Task.Delay(2000);
                try { await _hub.StartAsync(); } catch { }
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
            _hub.On("UpdateUpdater", async () =>
            {
                _logger.LogInformation("[SignalR] Received UpdateUpdater");
                await _mainWindow.CheckForUpdaterUpdates();
            });

            _hub.On("UpdateLauncher", () =>
            {
                _logger.LogInformation("[SignalR] Received UpdateLauncher");
                MainWindow.RestartLauncher();
            });

            _hub.On("UpdateGames", async () =>
            {
                _logger.LogInformation("[SignalR] Received UpdateGames");
                await _mainWindow.CheckForGameDatabaseChanges();
            });

            _hub.On<RegisteredPayload>("Registered", payload =>
            {
                _machineId = payload.MachineId;
                _siteId = payload.SiteId;
                _machineName = payload.MachineName ?? "Unknown";

                _logger.LogInformation(
                    "[SignalR] Registered as '{MachineName}' (ID: {MachineId}, Site: {SiteId})",
                    _machineName, _machineId, _siteId
                );
            });
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
            try { await _hub.StopAsync(); } catch { }
            _hub.DisposeAsync().AsTask().Wait(1000);
            _logger.LogInformation("[SignalR] Connection stopped");
        }

        public async Task SafeReportStatus(string status, string? ext = null)
        {
            _logger.LogInformation("[SignalR] Reporting status: {Status} {Ext}", status, ext ?? "");

            try { await _hub.InvokeAsync("ReportStatus", status, ext); }
            catch (Exception ex) { _logger.LogError(ex, "[SignalR] ReportStatus failed"); }
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