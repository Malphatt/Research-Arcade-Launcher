using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ArcademiaGameLauncher.Utils
{
    public static class WindowHelper
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
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags
        );

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        public static void ForceForeground(Window window)
        {
            if (window == null)
                return;

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                var windowHandle = new WindowInteropHelper(window).Handle;
                ForceForeground(windowHandle);
            });
        }

        public static void ForceForeground(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return;

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                IntPtr foregroundWnd = GetForegroundWindow();
                if (foregroundWnd != windowHandle)
                {
                    uint threadId1 = GetWindowThreadProcessId(foregroundWnd, IntPtr.Zero);
                    uint threadId2 = GetWindowThreadProcessId(windowHandle, IntPtr.Zero);

                    if (threadId1 != threadId2)
                    {
                        AttachThreadInput(threadId2, threadId1, true);
                        SetForegroundWindow(windowHandle);
                        SetWindowPos(
                            windowHandle,
                            HWND_TOPMOST,
                            0,
                            0,
                            0,
                            0,
                            SWP_NOMOVE | SWP_NOSIZE
                        );
                        AttachThreadInput(threadId2, threadId1, false);
                    }
                    else
                    {
                        SetForegroundWindow(windowHandle);
                        SetWindowPos(
                            windowHandle,
                            HWND_TOPMOST,
                            0,
                            0,
                            0,
                            0,
                            SWP_NOMOVE | SWP_NOSIZE
                        );
                    }
                }
            });
        }
    }
}
