using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace ArcademiaGameLauncher
{
    public partial class App : Application
    {
        private IHost _host = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                //.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .WriteTo.Console()
                .WriteTo.File(
                    "Logs/ArcadeClient-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 10,
                    fileSizeLimitBytes: 50 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true
                )
                .CreateLogger();

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSingleton<Windows.MainWindow>();
                    services.AddSingleton<Services.CreditsGenerator>();
                    services.AddSingleton<Services.GameDatabaseService>();
                })
                .Build();

            _host.Start();

            var mainWindow = _host.Services.GetRequiredService<Windows.MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
