using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ArcademiaGameLauncher.Windows
{
    public enum InfoWindowType
    {
        ForceExit,
        Idle,
    }

    public partial class InfoWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const uint SWP_SHOWWINDOW = 0x0040;

        private string _lastTimeString = "0.0";
        private IntPtr _windowHandle;

        private bool _isOpen = false;

        public InfoWindow()
        {
            InitializeComponent();
            ShowActivated = false;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHandle = new WindowInteropHelper(this).Handle;
        }

        public void ShowWindow(InfoWindowType type)
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                _isOpen = true;

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

                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;

                Topmost = false;
                Topmost = true;

                Show();
                Activate();

                ForceForeground();
            });
        }

        public void HideWindow()
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                _isOpen = false;

                if (_windowHandle != IntPtr.Zero)
                    SetWindowPos(_windowHandle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

                Topmost = false;
                Hide();

            }, System.Windows.Threading.DispatcherPriority.Send);
        }

        public void ForceForeground()
        {
            if (!_isOpen || _windowHandle == IntPtr.Zero) return;

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                if (!_isOpen) return;

                IntPtr foregroundWnd = GetForegroundWindow();
                if (foregroundWnd != _windowHandle)
                {
                    uint threadId1 = GetWindowThreadProcessId(foregroundWnd, IntPtr.Zero);
                    uint threadId2 = GetWindowThreadProcessId(_windowHandle, IntPtr.Zero);

                    if (threadId1 != threadId2)
                    {
                        AttachThreadInput(threadId2, threadId1, true);
                        SetForegroundWindow(_windowHandle);
                        SetWindowPos(_windowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                        AttachThreadInput(threadId2, threadId1, false);
                    }
                    else
                    {
                        SetForegroundWindow(_windowHandle);
                        SetWindowPos(_windowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                    }
                }
            });
        }

        public void SetCloseGameName(string gameName)
        {
            if (!_isOpen) return;
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                string text = gameName == null ? "Returning To Home Screen..." : $"Closing {gameName}...";
                ForceExitTitle.Text = text;
                IdleTitle.Text = text;
            });
        }

        public void UpdateCountdown(int time)
        {
            if (!_isOpen) return;
            float timeSeconds = time / 1000.0f;
            string timeString = Math.Max(0, timeSeconds).ToString("0.0");

            if (timeString == _lastTimeString) return;
            _lastTimeString = timeString;

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                if (ForceExitMenu.Visibility == Visibility.Visible)
                    ForceExitCountdown.Text = timeString;
                if (IdleMenu.Visibility == Visibility.Visible)
                    IdleCountdown.Text = timeString;
            });
        }
    }
}
