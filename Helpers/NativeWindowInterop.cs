using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// The two things a live alert needs from Windows and WPF does not offer.
    /// </summary>
    /// <remarks>
    /// Both exist for the same reason: the alert must be able to reach a dispatcher who is
    /// not looking at Raphael — in the phone system, in a spreadsheet — without ever taking
    /// the keyboard away from what they are typing.
    /// </remarks>
    public static class NativeWindowInterop
    {
        #region Taskbar flash

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        private const uint FLASHW_STOP = 0;
        private const uint FLASHW_TRAY = 2;
        private const uint FLASHW_TIMERNOFG = 12;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        /// <summary>
        /// Blinks the application's taskbar button until the window is brought forward.
        /// </summary>
        /// <remarks>
        /// Only for what needs somebody to act. A taskbar that blinks for every trip that
        /// starts stops meaning anything within an hour.
        /// </remarks>
        public static void FlashTaskbar(Window window)
        {
            try
            {
                if (window is null || window.IsActive)
                    return;

                var handle = new WindowInteropHelper(window).Handle;

                if (handle == IntPtr.Zero)
                    return;

                var info = new FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                    hwnd = handle,
                    dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
                    uCount = uint.MaxValue,
                    dwTimeout = 0
                };

                FlashWindowEx(ref info);
            }
            catch
            {
                // A blink that does not happen must not cost the alert.
            }
        }

        public static void StopFlashing(Window window)
        {
            try
            {
                if (window is null)
                    return;

                var handle = new WindowInteropHelper(window).Handle;

                if (handle == IntPtr.Zero)
                    return;

                var info = new FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                    hwnd = handle,
                    dwFlags = FLASHW_STOP,
                    uCount = 0,
                    dwTimeout = 0
                };

                FlashWindowEx(ref info);
            }
            catch
            {
            }
        }

        #endregion

        #region Never take the focus

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>
        /// Makes a window that can never become the active one.
        /// </summary>
        /// <remarks>
        /// ⚠️ Without this, a click on the alert card steals the keyboard from whatever the
        /// dispatcher was typing in — a patient's address in another program, mid-sentence.
        /// <c>ShowActivated=false</c> only covers the moment it appears; this covers every
        /// click afterwards. Opening the notice still brings Raphael forward, but because
        /// the code asks for it, which is what the dispatcher intended by clicking.
        /// </remarks>
        public static void MakeNonActivating(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;

                if (handle == IntPtr.Zero)
                    return;

                var style = GetWindowLong32(handle, GWL_EXSTYLE);

                SetWindowLong32(
                    handle,
                    GWL_EXSTYLE,
                    style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            }
            catch
            {
                // Worst case it behaves like an ordinary window.
            }
        }

        #endregion

        #region The monitor the main window is on

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        /// <summary>
        /// The usable area of the screen the given window is on, in WPF units.
        /// </summary>
        /// <remarks>
        /// ⚠️ Not <see cref="SystemParameters.WorkArea"/>, which only ever describes the
        /// primary monitor. A dispatcher with Raphael on the second screen would get the
        /// alert on the first one, which is the screen they are not looking at.
        /// </remarks>
        public static Rect WorkAreaFor(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;

                if (handle == IntPtr.Zero)
                    return SystemParameters.WorkArea;

                var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);

                if (monitor == IntPtr.Zero)
                    return SystemParameters.WorkArea;

                var info = new MONITORINFO
                {
                    cbSize = (uint)Marshal.SizeOf<MONITORINFO>()
                };

                if (!GetMonitorInfo(monitor, ref info))
                    return SystemParameters.WorkArea;

                var work = new Rect(
                    info.rcWork.Left,
                    info.rcWork.Top,
                    Math.Max(0, info.rcWork.Right - info.rcWork.Left),
                    Math.Max(0, info.rcWork.Bottom - info.rcWork.Top));

                return ToDeviceIndependent(window, work);
            }
            catch
            {
                return SystemParameters.WorkArea;
            }
        }

        /// <summary>
        /// Windows answers in physical pixels; WPF places windows in its own units. On a
        /// screen at 150% they are not the same number.
        /// </summary>
        private static Rect ToDeviceIndependent(Window window, Rect device)
        {
            var source = PresentationSource.FromVisual(window);

            var transform = source?.CompositionTarget?.TransformFromDevice;

            if (transform is null)
                return device;

            var topLeft = transform.Value.Transform(new Point(device.Left, device.Top));
            var bottomRight = transform.Value.Transform(new Point(device.Right, device.Bottom));

            return new Rect(topLeft, bottomRight);
        }

        #endregion
    }
}
