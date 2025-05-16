using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

public enum InfoWindowType
{
    ForceExit,
    Idle
}

namespace ArcademiaGameLauncher
{
    public partial class InfoWindow : Window
    {
        [DllImport("User32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        public InfoWindow() => InitializeComponent();

        public void ShowWindow(InfoWindowType type)
        {
            if (Application.Current == null || Application.Current.Dispatcher == null) return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ForceExitMenu.Visibility = Visibility.Collapsed;
                IdleMenu.Visibility = Visibility.Collapsed;

                switch (type)
                {
                    case InfoWindowType.ForceExit:
                        ForceExitMenu.Visibility = Visibility.Visible;
                        break;
                    case InfoWindowType.Idle:
                        IdleMenu.Visibility = Visibility.Visible;
                        break;
                }

                Show();

                SetForegroundWindow(new WindowInteropHelper(this).Handle);
            });
        }

        public void HideWindow()
        {
            if (Application.Current == null || Application.Current.Dispatcher == null) return;

            Application.Current?.Dispatcher?.Invoke(() => Hide());
        }

        public void SetCloseGameName(string gameName)
        {
            if (Application.Current == null || Application.Current.Dispatcher == null) return;

            if (gameName == null)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ForceExitTitle.Text = "Returning To Home Screen...";
                    IdleTitle.Text = "Returning To Home Screen...";
                });
            }
            else
            {
                string message = "Closing " + gameName.Substring(0, Math.Min(gameName.Length, 20)) + "...";

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ForceExitTitle.Text = message;
                    IdleTitle.Text = message;
                });
            }
        }

        public void UpdateCountdown(int time)
        {
            if (Application.Current == null || Application.Current.Dispatcher == null) return;

            float timeSeconds = time / 1000.0f;
            string timeString = Math.Max(0, timeSeconds).ToString("0.0");

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (ForceExitMenu.Visibility == Visibility.Visible)
                    ForceExitCountdown.Text = timeString;
                if (IdleMenu.Visibility == Visibility.Visible)
                    IdleCountdown.Text = timeString;
            });
        }
    }
}
