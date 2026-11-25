using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AuraShell.Localization;
using AuraShell.Native;
using AuraShell.Services;
using Microsoft.Win32;

namespace AuraShell.Modules.Widgets
{
    /// <summary>
    /// Apple macOS Sonoma / iOS 18 frosted-glass desktop widget host:
    /// - Exact Apple proportions: Small (172x172), Medium (360x172), Large (360x360), CornerRadius=24.
    /// - Real DWM Acrylic Blur + translucent tint (#CC1C1C1E Dark / #CCFFFFFF Light) + 1px specular rim.
    /// - True HWND_BOTTOM desktop pinning (stays behind normal app windows even when clicked).
    /// - Magnetic 16px snap-grid dragging with JSON persistence in config.json.
    /// - Right-click menu: Lock Position / Switch Size / Glass Theme / Opacity / Hide Widget.
    /// </summary>
    public partial class AppleWidgetHostWindow : Window
    {
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const uint SWP_NOZORDER = 0x0004;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const double GridStep = 16.0;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        private readonly ConfigStore _configStore;
        private readonly WidgetPlacement _placement;
        private readonly UserControl _cardControl;
        private readonly IAppleWidgetCard _card;

        private HwndSource? _hwndSource;
        private bool _isDragging;
        private Point _dragCursorStartScreen;
        private double _dragWinStartLeft;
        private double _dragWinStartTop;

        public string WidgetId => _card.WidgetId;
        public WidgetPlacement Placement => _placement;

        public AppleWidgetHostWindow(ConfigStore configStore, WidgetPlacement placement, UserControl cardControl, IAppleWidgetCard card)
        {
            InitializeComponent();
            _configStore = configStore;
            _placement = placement;
            _cardControl = cardControl;
            _card = card;

            CardPresenter.Content = _cardControl;
            FlowDirection = LocaleManager.CurrentFlowDirection;

            Left = SnapToGrid(_placement.X);
            Top = SnapToGrid(_placement.Y);

            ApplySize(ParseSize(_placement.Size), save: false);
            ApplyAppearance();
            RebuildContextMenu();

            LocaleManager.LocaleChanged += (_, _) => Dispatcher.Invoke(RebuildContextMenu);
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            Win32Shell.PinToDesktopBottom(this);
            DwmGlass.SetRoundedCorners(this, DwmGlass.CornerPreference.Round);
            ApplyDwmGlassBackdrop();

            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(WndProc);
        }

        protected override void OnClosed(EventArgs e)
        {
            _hwndSource?.RemoveHook(WndProc);
            base.OnClosed(e);
        }

        /// <summary>
        /// Enforces true macOS Sonoma desktop pinning: intercepts WM_WINDOWPOSCHANGING so
        /// clicking or dragging a widget keeps it strictly at HWND_BOTTOM behind normal windows.
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero)
            {
                var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((wp.flags & SWP_NOZORDER) == 0)
                {
                    wp.hwndInsertAfter = HWND_BOTTOM;
                    Marshal.StructureToPtr(wp, lParam, false);
                }
            }
            return IntPtr.Zero;
        }

        #region Sizing, Theme & DWM Glass

        public static AppleWidgetSize ParseSize(string sizeName)
        {
            return sizeName?.ToLowerInvariant() switch
            {
                "medium" => AppleWidgetSize.Medium,
                "large" => AppleWidgetSize.Large,
                _ => AppleWidgetSize.Small
            };
        }

        public void ApplySize(AppleWidgetSize size, bool save = true)
        {
            switch (size)
            {
                case AppleWidgetSize.Medium:
                    Width = 360;
                    Height = 172;
                    _placement.Size = "Medium";
                    break;
                case AppleWidgetSize.Large:
                    Width = 360;
                    Height = 360;
                    _placement.Size = "Large";
                    break;
                default:
                    Width = 172;
                    Height = 172;
                    _placement.Size = "Small";
                    break;
            }

            _card.ApplySize(size);
            ClampToWorkArea();

            if (save)
                _configStore.Save();
        }

        public void ApplyAppearance()
        {
            bool isDark = ResolveIsDarkTheme(_placement.Theme);
            double op = Math.Clamp(_placement.Opacity, 0.45, 1.0);
            Opacity = op;

            if (_card.UsesCustomSkyBackdrop)
            {
                GlassSurface.Background = Brushes.Transparent;
            }
            else
            {
                // Apple HIG translucent frosted tint (#CC1C1C1E Dark / #CCFFFFFF Light)
                GlassSurface.Background = isDark
                    ? new SolidColorBrush(Color.FromArgb(0xCC, 0x1C, 0x1C, 0x1E))
                    : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
            }

            // Razor-thin 1px specular top-edge glass highlight
            var rim = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            if (isDark || _card.UsesCustomSkyBackdrop)
            {
                rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 0.0));
                rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.25));
                rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF), 1.0));
            }
            else
            {
                rim.GradientStops.Add(new GradientStop(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF), 0.0));
                rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF), 0.35));
                rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 0x00, 0x00, 0x00), 1.0));
            }
            SpecularRim.BorderBrush = rim;

            LockIndicator.Visibility = _placement.IsLocked ? Visibility.Visible : Visibility.Collapsed;
            _card.ApplyTheme(isDark);

            if (IsLoaded)
                ApplyDwmGlassBackdrop();
        }

        private void ApplyDwmGlassBackdrop()
        {
            if (_card.UsesCustomSkyBackdrop) return;
            bool isDark = ResolveIsDarkTheme(_placement.Theme);
            DwmGlass.EnableImmersiveDarkMode(this, isDark);

            if (isDark)
                DwmGlass.EnableAcrylicBlur(this, 0xB8, 0x1C, 0x1C, 0x1E);
            else
                DwmGlass.EnableAcrylicBlur(this, 0xB8, 0xF5, 0xF5, 0xF7);
        }

        public static bool ResolveIsDarkTheme(string themeMode)
        {
            if (string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase))
                return true;

            // Auto: inspect Windows AppsUseLightTheme registry value
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int val)
                    return val == 0;
            }
            catch { }

            return true;
        }

        #endregion

        #region Magnetic 16px Snap-Grid Dragging

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_placement.IsLocked) return;
            // Don't hijack clicks on interactive controls (buttons, textboxes, checkboxes)
            if (IsInteractiveOrigin(e.OriginalSource as DependencyObject))
                return;

            _isDragging = true;
            _dragCursorStartScreen = PointToScreen(e.GetPosition(this));
            _dragWinStartLeft = Left;
            _dragWinStartTop = Top;
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            Point curScreen = PointToScreen(e.GetPosition(this));

            var src = PresentationSource.FromVisual(this);
            double dpiX = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            double dx = (curScreen.X - _dragCursorStartScreen.X) / dpiX;
            double dy = (curScreen.Y - _dragCursorStartScreen.Y) / dpiY;

            // Magnetic 16px snap-grid
            Left = SnapToGrid(_dragWinStartLeft + dx);
            Top = SnapToGrid(_dragWinStartTop + dy);
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            ReleaseMouseCapture();

            ClampToWorkArea();
            _placement.X = Left;
            _placement.Y = Top;
            _configStore.Save();
            Win32Shell.PinToDesktopBottom(this);
        }

        private double SnapToGrid(double value) => _configStore.Current.WidgetSnapToGrid ? Math.Round(value / GridStep) * GridStep : Math.Round(value);

        private void ClampToWorkArea()
        {
            var area = SystemParameters.WorkArea;
            Left = SnapToGrid(Math.Clamp(Left, area.Left + 8, Math.Max(area.Left + 8, area.Right - Width - 8)));
            Top = SnapToGrid(Math.Clamp(Top, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - Height - 8)));
        }

        private static bool IsInteractiveOrigin(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is Button || source is TextBox || source is CheckBox || source is Slider)
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        #endregion

        #region Right-Click Context Menu

        private void RebuildContextMenu()
        {
            var menu = new ContextMenu();

            // 1. Lock / Unlock Position
            var lockItem = new MenuItem
            {
                Header = _placement.IsLocked
                    ? LocaleManager.Get("WidgetUnlockPosition", "Unlock Position")
                    : LocaleManager.Get("WidgetLockPosition", "Lock Position")
            };
            lockItem.Click += (_, _) =>
            {
                _placement.IsLocked = !_placement.IsLocked;
                _configStore.Save();
                ApplyAppearance();
                RebuildContextMenu();
            };
            menu.Items.Add(lockItem);

            // 2. Switch Size submenu
            var sizeMenu = new MenuItem { Header = LocaleManager.Get("WidgetSwitchSize", "Switch Size") };
            AddSizeItem(sizeMenu, LocaleManager.Get("WidgetSizeSmall", "Small (172×172)"), AppleWidgetSize.Small);
            AddSizeItem(sizeMenu, LocaleManager.Get("WidgetSizeMedium", "Medium (360×172)"), AppleWidgetSize.Medium);
            AddSizeItem(sizeMenu, LocaleManager.Get("WidgetSizeLarge", "Large (360×360)"), AppleWidgetSize.Large);
            menu.Items.Add(sizeMenu);

            // 3. Glass Theme submenu
            var themeMenu = new MenuItem { Header = LocaleManager.Get("WidgetThemeMenu", "Glass Theme") };
            AddThemeItem(themeMenu, LocaleManager.Get("WidgetThemeDark", "Dark Glass"), "Dark");
            AddThemeItem(themeMenu, LocaleManager.Get("WidgetThemeLight", "Light Glass"), "Light");
            AddThemeItem(themeMenu, LocaleManager.Get("WidgetThemeAuto", "Auto (Windows)"), "Auto");
            menu.Items.Add(themeMenu);

            // 4. Opacity submenu
            var opacityMenu = new MenuItem { Header = LocaleManager.Get("WidgetOpacityMenu", "Opacity") };
            foreach (int pct in new[] { 100, 92, 82, 70 })
            {
                int captured = pct;
                var opItem = new MenuItem
                {
                    Header = $"{pct}%",
                    IsChecked = Math.Abs(_placement.Opacity - pct / 100.0) < 0.04
                };
                opItem.Click += (_, _) =>
                {
                    _placement.Opacity = captured / 100.0;
                    _configStore.Save();
                    ApplyAppearance();
                    RebuildContextMenu();
                };
                opacityMenu.Items.Add(opItem);
            }
            menu.Items.Add(opacityMenu);

            menu.Items.Add(new Separator());

            // 5. Hide Widget
            var hideItem = new MenuItem { Header = LocaleManager.Get("WidgetHide", "Hide Widget") };
            hideItem.Click += (_, _) =>
            {
                _placement.IsVisible = false;
                _configStore.Save();
                Hide();
            };
            menu.Items.Add(hideItem);

            ContextMenu = menu;
        }

        private void AddSizeItem(MenuItem parent, string header, AppleWidgetSize size)
        {
            var item = new MenuItem
            {
                Header = header,
                IsChecked = ParseSize(_placement.Size) == size
            };
            item.Click += (_, _) =>
            {
                ApplySize(size, save: true);
                RebuildContextMenu();
            };
            parent.Items.Add(item);
        }

        private void AddThemeItem(MenuItem parent, string header, string theme)
        {
            var item = new MenuItem
            {
                Header = header,
                IsChecked = string.Equals(_placement.Theme, theme, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) =>
            {
                _placement.Theme = theme;
                _configStore.Save();
                ApplyAppearance();
                RebuildContextMenu();
            };
            parent.Items.Add(item);
        }

        #endregion
    }
}
