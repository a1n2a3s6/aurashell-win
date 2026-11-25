using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AuraShell.Native
{
    /// <summary>
    /// Provides real Windows 10/11 DWM Acrylic Blur via SetWindowCompositionAttribute
    /// and native rounded corners / dark mode / border styling via DwmSetWindowAttribute.
    /// </summary>
    public static class DwmGlass
    {
        private const int WCA_ACCENT_POLICY = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        public enum AccentState : int
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        public enum CornerPreference : int
        {
            Default = 0,
            DoNotRound = 1,
            Round = 2,
            RoundSmall = 3
        }

        public enum SystemBackdropType : int
        {
            Auto = 0,
            None = 1,
            MainWindow = 2,      // Mica
            TransientWindow = 3, // Acrylic
            TabbedWindow = 4     // Mica Alt
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public uint GradientColor; // AABBGGRR
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        /// <summary>
        /// Converts ARGB components to Windows ABGR uint format used by AccentPolicy.GradientColor.
        /// </summary>
        public static uint ToAbgr(byte a, byte r, byte g, byte b)
        {
            return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
        }

        /// <summary>
        /// Enables real Acrylic Blur behind a WPF Window using SetWindowCompositionAttribute.
        /// </summary>
        /// <param name="window">Target WPF Window (must have an initialized HWND or call on SourceInitialized).</param>
        /// <param name="alpha">Opacity byte (0-255).</param>
        /// <param name="r">Red tint byte.</param>
        /// <param name="g">Green tint byte.</param>
        /// <param name="b">Blue tint byte.</param>
        public static bool EnableAcrylicBlur(Window window, byte alpha = 0xCC, byte r = 0x0B, byte g = 0x0D, byte b = 0x14)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;
            return EnableAcrylicBlur(hwnd, ToAbgr(alpha, r, g, b));
        }

        /// <summary>
        /// Enables real Acrylic Blur on an existing HWND.
        /// </summary>
        public static bool EnableAcrylicBlur(IntPtr hwnd, uint abgrTint)
        {
            if (hwnd == IntPtr.Zero) return false;

            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2, // Draw all borders cleanly
                GradientColor = abgrTint,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    SizeOfData = size,
                    Data = accentPtr
                };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }

        /// <summary>
        /// Disables custom Acrylic/Blur composition on the window.
        /// </summary>
        public static bool DisableBlur(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_DISABLED,
                AccentFlags = 0,
                GradientColor = 0,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    SizeOfData = size,
                    Data = accentPtr
                };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }

        /// <summary>
        /// Applies native Windows 11 DWM rounded corners via DwmSetWindowAttribute.
        /// </summary>
        public static bool SetRoundedCorners(Window window, CornerPreference preference = CornerPreference.Round)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;
            int pref = (int)preference;
            return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0;
        }

        /// <summary>
        /// Enables immersive dark mode frame styling via DwmSetWindowAttribute.
        /// </summary>
        public static bool EnableImmersiveDarkMode(Window window, bool enabled = true)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;
            int value = enabled ? 1 : 0;
            return DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) == 0;
        }

        /// <summary>
        /// Sets the Windows 11 DWM border color (0x00BBGGRR, or 0xFFFFFFFE for none).
        /// </summary>
        public static bool SetBorderColor(Window window, int bgrColor)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;
            return DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref bgrColor, sizeof(int)) == 0;
        }

        /// <summary>
        /// Applies Windows 11 22H2+ SystemBackdropType (Mica / Acrylic / Tabbed) if supported.
        /// </summary>
        public static bool SetSystemBackdrop(Window window, SystemBackdropType backdrop)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;
            int value = (int)backdrop;
            return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int)) == 0;
        }
    }
}
