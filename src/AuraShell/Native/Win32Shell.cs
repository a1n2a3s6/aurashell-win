using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AuraShell.Native
{
    /// <summary>
    /// Native Win32 shell utilities: fullscreen/maximized detector, HWND_BOTTOM desktop pinning,
    /// window & file icon extraction, media/audio key simulation, Core Audio volume & mic mute,
    /// pixel color eyedropper, keep-awake, recycle bin, lock PC, Windows taskbar toggle,
    /// startup registration, and clipboard listener hooks. Zero external NuGet dependencies.
    /// </summary>
    public static class Win32Shell
    {
        #region Constants & Structs

        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_NOREPEAT = 0x4000;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private const uint WM_GETICON = 0x007F;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const int ICON_SMALL2 = 2;
        private const int GCLP_HICON = -14;
        private const int GCLP_HICONSM = -34;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        private const byte VK_VOLUME_MUTE = 0xAD;
        private const byte VK_VOLUME_DOWN = 0xAE;
        private const byte VK_VOLUME_UP = 0xAF;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [Flags]
        public enum ExecutionState : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        #endregion

        #region P/Invoke Declarations

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetClassLong")]
        private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool LockWorkStation();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
            IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, nIndex) : new IntPtr(unchecked((int)GetClassLong32(hWnd, nIndex)));

        #endregion

        #region Fullscreen & Maximized Detector

        private static readonly int CurrentProcessId = Process.GetCurrentProcess().Id;

        private static bool IsDesktopShellClass(string cls) =>
            cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";

        /// <summary>
        /// Returns true if another application (game, YouTube fullscreen video, media player)
        /// currently occupies the full bounds of its monitor.
        /// </summary>
        public static bool IsFullscreenAppActive()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || !IsWindowVisible(fg) || IsIconic(fg))
                return false;

            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)CurrentProcessId)
                return false;

            var classBuilder = new StringBuilder(256);
            GetClassName(fg, classBuilder, classBuilder.Capacity);
            if (IsDesktopShellClass(classBuilder.ToString()))
                return false;

            if (!GetWindowRect(fg, out RECT winRect))
                return false;

            IntPtr monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return false;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info))
                return false;

            const int tolerance = 2;
            return winRect.Left <= info.rcMonitor.Left + tolerance &&
                   winRect.Top <= info.rcMonitor.Top + tolerance &&
                   winRect.Right >= info.rcMonitor.Right - tolerance &&
                   winRect.Bottom >= info.rcMonitor.Bottom - tolerance;
        }

        /// <summary>
        /// Returns true if the foreground app is maximized or fullscreen (used by AeroDock Auto-Hide).
        /// </summary>
        public static bool IsMaximizedOrFullscreenAppActive()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || !IsWindowVisible(fg) || IsIconic(fg))
                return false;

            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)CurrentProcessId)
                return false;

            var classBuilder = new StringBuilder(256);
            GetClassName(fg, classBuilder, classBuilder.Capacity);
            if (IsDesktopShellClass(classBuilder.ToString()))
                return false;

            if (IsZoomed(fg))
                return true;

            return IsFullscreenAppActive();
        }

        #endregion

        #region Desktop Pinning, Taskbar Toggle & Startup Registry

        public static void PinToDesktopBottom(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            HideFromAltTab(window);
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }

        public static void EnsureTopmostNoActivate(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public static void HideFromAltTab(Window window, bool noActivate = false)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_TOOLWINDOW;
            if (noActivate) exStyle |= WS_EX_NOACTIVATE;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
        }

        /// <summary>
        /// Safely hides or restores the native Windows Taskbar (Shell_TrayWnd &amp; Shell_SecondaryTrayWnd).
        /// Always call SetWindowsTaskbarVisible(true) on application exit!
        /// </summary>
        public static void SetWindowsTaskbarVisible(bool visible)
        {
            int cmd = visible ? SW_SHOW : SW_HIDE;
            IntPtr primary = FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero) ShowWindow(primary, cmd);

            IntPtr secondary = FindWindow("Shell_SecondaryTrayWnd", null);
            if (secondary != IntPtr.Zero) ShowWindow(secondary, cmd);
        }

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "AuraShell";

        public static bool GetLaunchWithWindows()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                return key?.GetValue(StartupValueName) is string val && !string.IsNullOrWhiteSpace(val);
            }
            catch
            {
                return false;
            }
        }

        public static void SetLaunchWithWindows(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null) return;

                if (enabled)
                {
                    string? exe = Environment.ProcessPath;
                    if (!string.IsNullOrWhiteSpace(exe))
                        key.SetValue(StartupValueName, $"\"{exe}\"");
                }
                else
                {
                    key.DeleteValue(StartupValueName, false);
                }
            }
            catch { }
        }

        #endregion

        #region Window & File Icon Extractor

        public static ImageSource? ExtractWindowIcon(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;

            IntPtr hIcon = IntPtr.Zero;
            SendMessageTimeout(hwnd, WM_GETICON, new IntPtr(ICON_BIG), IntPtr.Zero, SMTO_ABORTIFHUNG, 150, out hIcon);
            if (hIcon == IntPtr.Zero)
                SendMessageTimeout(hwnd, WM_GETICON, new IntPtr(ICON_SMALL2), IntPtr.Zero, SMTO_ABORTIFHUNG, 150, out hIcon);
            if (hIcon == IntPtr.Zero)
                SendMessageTimeout(hwnd, WM_GETICON, new IntPtr(ICON_SMALL), IntPtr.Zero, SMTO_ABORTIFHUNG, 150, out hIcon);
            if (hIcon == IntPtr.Zero)
                hIcon = GetClassLongPtr(hwnd, GCLP_HICON);
            if (hIcon == IntPtr.Zero)
                hIcon = GetClassLongPtr(hwnd, GCLP_HICONSM);

            if (hIcon != IntPtr.Zero)
            {
                try
                {
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    return bmp;
                }
                catch { }
            }

            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                using var proc = Process.GetProcessById((int)pid);
                string? exePath = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    return ExtractFileIcon(exePath);
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Extracts a crisp high-res OS icon for any .exe, .lnk, or file path via SHGetFileInfo / ExtractAssociatedIcon.
        /// </summary>
        public static ImageSource? ExtractFileIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                string resolved = ResolveExecutablePath(path);
                if (File.Exists(resolved) || Directory.Exists(resolved))
                {
                    var shfi = new SHFILEINFO();
                    IntPtr res = SHGetFileInfo(resolved, 0, out shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
                    if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                    {
                        try
                        {
                            var bmp = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            bmp.Freeze();
                            return bmp;
                        }
                        finally
                        {
                            DestroyIcon(shfi.hIcon);
                        }
                    }

                    if (File.Exists(resolved))
                    {
                        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(resolved);
                        if (icon != null)
                        {
                            var bmp = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            bmp.Freeze();
                            return bmp;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        public static string ResolveExecutablePath(string commandOrPath)
        {
            if (string.IsNullOrWhiteSpace(commandOrPath)) return commandOrPath;
            string expanded = Environment.ExpandEnvironmentVariables(commandOrPath.Trim().Trim('"'));
            if (File.Exists(expanded) || Directory.Exists(expanded)) return expanded;

            // Search common Windows directories and PATH
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sysDir = Environment.SystemDirectory;
            foreach (string candidate in new[]
            {
                Path.Combine(winDir, expanded),
                Path.Combine(sysDir, expanded)
            })
            {
                if (File.Exists(candidate)) return candidate;
            }

            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), expanded);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            }

            return expanded;
        }

        #endregion

        #region Media & Audio Key Simulation + Core Audio COM Interop

        private static void SendVirtualKey(byte vk)
        {
            keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void SimulateMediaPlayPause() => SendVirtualKey(VK_MEDIA_PLAY_PAUSE);
        public static void SimulateMediaNext() => SendVirtualKey(VK_MEDIA_NEXT_TRACK);
        public static void SimulateMediaPrevious() => SendVirtualKey(VK_MEDIA_PREV_TRACK);
        public static void SimulateVolumeMute() => SendVirtualKey(VK_VOLUME_MUTE);
        public static void SimulateVolumeUp() => SendVirtualKey(VK_VOLUME_UP);
        public static void SimulateVolumeDown() => SendVirtualKey(VK_VOLUME_DOWN);

        private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
        private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        private static IAudioEndpointVolume? GetEndpointVolume(EDataFlow flow)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                int hr = enumerator.GetDefaultAudioEndpoint(flow, ERole.eMultimedia, out IMMDevice device);
                if (hr != 0 || device == null) return null;

                Guid iid = typeof(IAudioEndpointVolume).GUID;
                hr = device.Activate(ref iid, 23, IntPtr.Zero, out object obj);
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
                return hr == 0 ? obj as IAudioEndpointVolume : null;
            }
            catch
            {
                return null;
            }
        }

        public static float GetMasterVolume()
        {
            var vol = GetEndpointVolume(EDataFlow.eRender);
            if (vol == null) return 0.5f;
            try
            {
                vol.GetMasterVolumeLevelScalar(out float level);
                return Math.Clamp(level, 0f, 1f);
            }
            finally { Marshal.ReleaseComObject(vol); }
        }

        public static void SetMasterVolume(float level0To1)
        {
            var vol = GetEndpointVolume(EDataFlow.eRender);
            if (vol == null) return;
            try
            {
                Guid ctx = Guid.Empty;
                vol.SetMasterVolumeLevelScalar(Math.Clamp(level0To1, 0f, 1f), ref ctx);
            }
            finally { Marshal.ReleaseComObject(vol); }
        }

        public static bool GetMasterMute()
        {
            var vol = GetEndpointVolume(EDataFlow.eRender);
            if (vol == null) return false;
            try
            {
                vol.GetMute(out bool muted);
                return muted;
            }
            finally { Marshal.ReleaseComObject(vol); }
        }

        public static bool ToggleMasterMute()
        {
            var vol = GetEndpointVolume(EDataFlow.eRender);
            if (vol == null)
            {
                SimulateVolumeMute();
                return false;
            }
            try
            {
                vol.GetMute(out bool muted);
                Guid ctx = Guid.Empty;
                vol.SetMute(!muted, ref ctx);
                return !muted;
            }
            finally { Marshal.ReleaseComObject(vol); }
        }

        public static bool GetMicMute()
        {
            var vol = GetEndpointVolume(EDataFlow.eCapture);
            if (vol == null) return false;
            try
            {
                vol.GetMute(out bool muted);
                return muted;
            }
            finally { Marshal.ReleaseComObject(vol); }
        }

        public static bool ToggleMicMute()
        {
            var vol = GetEndpointVolume(EDataFlow.eCapture);
            if (vol == null) return false;
            try
            {
                vol.GetMute(out bool muted);
                Guid ctx = Guid.Empty;
                vol.SetMute(!muted, ref ctx);
                return !muted;
            }
            finally { Marshal.ReleaseComObject(vol); }
        }

        #endregion

        #region Pixel Color Eyedropper & System Actions

        public static Color GetPixelColorAt(int x, int y)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return Colors.Black;
            try
            {
                uint pixel = GetPixel(hdc, x, y);
                byte r = (byte)(pixel & 0x000000FF);
                byte g = (byte)((pixel & 0x0000FF00) >> 8);
                byte b = (byte)((pixel & 0x00FF0000) >> 16);
                return Color.FromRgb(r, g, b);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        public static (Color Color, string Hex) GetCursorPixelColor()
        {
            GetCursorPos(out POINT pt);
            Color c = GetPixelColorAt(pt.X, pt.Y);
            return (c, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
        }

        public static void SetKeepAwake(bool enabled)
        {
            if (enabled)
                SetThreadExecutionState(ExecutionState.ES_CONTINUOUS | ExecutionState.ES_SYSTEM_REQUIRED | ExecutionState.ES_DISPLAY_REQUIRED);
            else
                SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
        }

        public static bool EmptyRecycleBin()
        {
            try
            {
                uint result = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                return result == 0 || result == 0x8000FFFF;
            }
            catch
            {
                return false;
            }
        }

        public static void OpenSettingsUri(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch { }
        }

        #endregion
    }
}
