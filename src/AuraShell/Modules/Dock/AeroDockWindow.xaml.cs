using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using AuraShell.Localization;
using AuraShell.Modules.Widgets;
using AuraShell.Native;
using AuraShell.Services;

namespace AuraShell.Modules.Dock
{
    /// <summary>
    /// AeroDock: Floating macOS frosted-glass bottom dock with 60fps parabolic cosine magnification,
    /// drag-and-drop .exe/.lnk pinning, real OS icon extraction, running-app indicator dots,
    /// Downloads Stack, Recycle Bin, Settings icon, smart Auto-Hide, and optional Windows Taskbar hide.
    /// </summary>
    public partial class AeroDockWindow : Window
    {
        private sealed class DockSlot
        {
            public string Label { get; set; } = string.Empty;
            public string ProcessName { get; set; } = string.Empty;
            public string TargetPath { get; set; } = string.Empty;
            public bool IsPinnedApp { get; set; }
            public string? PinId { get; set; }
            public Grid Container { get; init; } = null!;
            public Border IconBox { get; init; } = null!;
            public Ellipse RunningDot { get; init; } = null!;
            public double CurrentSize { get; set; } = 44;
        }

        private readonly ConfigStore _configStore;
        private readonly Action _openStudio;
        private readonly List<DockSlot> _slots = new();
        private readonly DispatcherTimer _statusTimer;

        private bool _isPointerInside;
        private double _pointerX;
        private double _autoHideCurrentY;
        private double _autoHideTargetY;
        private bool _isAutoHidden;
        private bool _isDark = true;

        public AeroDockWindow(ConfigStore configStore, Action openStudio)
        {
            InitializeComponent();
            _configStore = configStore;
            _openStudio = openStudio;

            FlowDirection = LocaleManager.CurrentFlowDirection;
            PositionBottomCenter();

            RebuildDockItems();
            ApplyTheme();

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _statusTimer.Tick += OnStatusTick;
            _statusTimer.Start();

            CompositionTarget.Rendering += OnAnimationFrame;
            LocaleManager.LocaleChanged += (_, _) => Dispatcher.Invoke(RebuildDockItems);
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            Win32Shell.HideFromAltTab(this);
            DwmGlass.SetRoundedCorners(this, DwmGlass.CornerPreference.Round);
            ApplyDwmGlass();
        }

        protected override void OnClosed(EventArgs e)
        {
            CompositionTarget.Rendering -= OnAnimationFrame;
            _statusTimer.Stop();
            base.OnClosed(e);
        }

        public void PositionBottomCenter()
        {
            Width = Math.Min(1200, SystemParameters.PrimaryScreenWidth);
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2.0;
            Top = SystemParameters.PrimaryScreenHeight - Height;
        }

        public void ApplyTheme()
        {
            _isDark = AppleWidgetHostWindow.ResolveIsDarkTheme(_configStore.Current.WidgetTheme);
            DockPill.Background = _isDark
                ? new SolidColorBrush(Color.FromArgb(0xC8, 0x18, 0x1A, 0x22))
                : new SolidColorBrush(Color.FromArgb(0xCC, 0xF4, 0xF5, 0xF8));

            if (IsLoaded) ApplyDwmGlass();
        }

        private void ApplyDwmGlass()
        {
            if (_isDark)
                DwmGlass.EnableAcrylicBlur(this, 0x90, 0x16, 0x18, 0x22);
            else
                DwmGlass.EnableAcrylicBlur(this, 0x90, 0xF2, 0xF4, 0xF8);
        }

        public void RebuildDockItems()
        {
            DockItemsPanel.Children.Clear();
            _slots.Clear();

            double baseSize = Math.Clamp(_configStore.Current.DockIconSize, 34, 60);

            // 1. Pinned OS Applications
            foreach (var pin in _configStore.Current.DockPins)
            {
                ImageSource? icon = Win32Shell.ExtractFileIcon(pin.TargetPath);
                var slot = CreateSlot(
                    label: pin.Name,
                    processName: pin.ProcessName,
                    targetPath: pin.TargetPath,
                    icon: icon,
                    fallbackEmoji: pin.FallbackEmoji,
                    baseSize: baseSize,
                    isPinnedApp: true,
                    pinId: pin.Id,
                    onClick: () => LaunchTarget(pin.TargetPath),
                    contextMenu: BuildPinnedAppContextMenu(pin));

                _slots.Add(slot);
                DockItemsPanel.Children.Add(slot.Container);
            }

            // 2. Vertical Glass Separator
            var sep = new Border
            {
                Width = 1.2,
                Height = baseSize * 0.72,
                Background = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(8, 0, 8, 8),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            DockItemsPanel.Children.Add(sep);

            // 3. Quick Downloads Stack
            var dlSlot = CreateSlot(
                label: LocaleManager.Get("DockDownloads", "Downloads"),
                processName: string.Empty,
                targetPath: GetDownloadsPath(),
                icon: Win32Shell.ExtractFileIcon(GetDownloadsPath()),
                fallbackEmoji: "📥",
                baseSize: baseSize,
                isPinnedApp: false,
                pinId: null,
                onClick: ToggleDownloadsStack,
                contextMenu: BuildDownloadsContextMenu());
            _slots.Add(dlSlot);
            DockItemsPanel.Children.Add(dlSlot.Container);

            // 4. Recycle Bin
            var binSlot = CreateSlot(
                label: LocaleManager.Get("DockRecycleBin", "Recycle Bin"),
                processName: string.Empty,
                targetPath: "shell:RecycleBinFolder",
                icon: null,
                fallbackEmoji: "🗑️",
                baseSize: baseSize,
                isPinnedApp: false,
                pinId: null,
                onClick: () => LaunchTarget("shell:RecycleBinFolder"),
                contextMenu: BuildRecycleBinContextMenu());
            _slots.Add(binSlot);
            DockItemsPanel.Children.Add(binSlot.Container);

            // 5. AuraShell Studio Settings
            var studioSlot = CreateSlot(
                label: LocaleManager.Get("DockSettings", "AuraShell Studio"),
                processName: string.Empty,
                targetPath: string.Empty,
                icon: null,
                fallbackEmoji: "⚙️",
                baseSize: baseSize,
                isPinnedApp: false,
                pinId: null,
                onClick: _openStudio,
                contextMenu: null);
            _slots.Add(studioSlot);
            DockItemsPanel.Children.Add(studioSlot.Container);

            UpdateRunningIndicators();
        }

        private DockSlot CreateSlot(
            string label,
            string processName,
            string targetPath,
            ImageSource? icon,
            string fallbackEmoji,
            double baseSize,
            bool isPinnedApp,
            string? pinId,
            Action onClick,
            ContextMenu? contextMenu)
        {
            var container = new Grid
            {
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.Hand,
                ContextMenu = contextMenu
            };
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });

            var iconBox = new Border
            {
                Width = baseSize,
                Height = baseSize,
                CornerRadius = new CornerRadius(12),
                Background = icon != null
                    ? new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x30, 0x38, 0xBD, 0xF8)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Bottom,
                ClipToBounds = true
            };

            if (icon != null)
            {
                iconBox.Child = new Image
                {
                    Source = icon,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(5)
                };
            }
            else
            {
                iconBox.Child = new TextBlock
                {
                    Text = fallbackEmoji,
                    FontSize = baseSize * 0.52,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            Grid.SetRow(iconBox, 0);
            container.Children.Add(iconBox);

            // Live glowing indicator dot under running apps
            var dot = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = Visibility.Hidden,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x38, 0xBD, 0xF8),
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.9
                }
            };
            Grid.SetRow(dot, 1);
            container.Children.Add(dot);

            container.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                onClick();
            };

            return new DockSlot
            {
                Label = label,
                ProcessName = processName,
                TargetPath = targetPath,
                IsPinnedApp = isPinnedApp,
                PinId = pinId,
                Container = container,
                IconBox = iconBox,
                RunningDot = dot,
                CurrentSize = baseSize
            };
        }

        #region 60fps Parabolic Cosine Magnification & Auto-Hide

        private void OnAnimationFrame(object? sender, EventArgs e)
        {
            // 1. Smooth auto-hide vertical glide
            _autoHideCurrentY += (_autoHideTargetY - _autoHideCurrentY) * 0.22;
            if (Math.Abs(_autoHideTargetY - _autoHideCurrentY) < 0.3)
                _autoHideCurrentY = _autoHideTargetY;
            AutoHideTransform.Y = _autoHideCurrentY;

            // 2. Parabolic Cosine Magnification across dock icons
            double baseSize = Math.Clamp(_configStore.Current.DockIconSize, 34, 60);
            double maxMag = Math.Clamp(_configStore.Current.DockMagnification, 1.0, 1.85);
            double maxDelta = baseSize * (maxMag - 1.0);
            const double influenceRadius = 115.0;

            DockSlot? closestSlot = null;
            double closestDist = double.MaxValue;
            Point closestTopCenter = default;

            foreach (var slot in _slots)
            {
                double targetSize = baseSize;

                if (_isPointerInside && maxDelta > 0.5)
                {
                    try
                    {
                        Point center = slot.IconBox.TranslatePoint(
                            new Point(slot.IconBox.ActualWidth / 2.0, 0), this);
                        double dist = Math.Abs(_pointerX - center.X);

                        if (dist < influenceRadius)
                        {
                            // Smooth parabolic cosine bell curve
                            double ratio = Math.Cos((dist / influenceRadius) * (Math.PI / 2.0));
                            targetSize = baseSize + maxDelta * (ratio * ratio);
                        }

                        if (dist < closestDist && dist < slot.IconBox.ActualWidth * 0.65)
                        {
                            closestDist = dist;
                            closestSlot = slot;
                            closestTopCenter = center;
                        }
                    }
                    catch { }
                }

                slot.CurrentSize += (targetSize - slot.CurrentSize) * 0.28;
                slot.IconBox.Width = slot.CurrentSize;
                slot.IconBox.Height = slot.CurrentSize;
                slot.IconBox.CornerRadius = new CornerRadius(Math.Clamp(slot.CurrentSize * 0.26, 10, 18));
            }

            // 3. Position floating dark tooltip pill directly above hovered icon
            if (_isPointerInside && closestSlot != null)
            {
                HoverTooltipText.Text = closestSlot.Label;
                HoverTooltipPill.Visibility = Visibility.Visible;
                HoverTooltipPill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double tipW = HoverTooltipPill.DesiredSize.Width;
                double tipH = HoverTooltipPill.DesiredSize.Height;

                Canvas.SetLeft(HoverTooltipPill, Math.Clamp(closestTopCenter.X - tipW / 2.0, 8, ActualWidth - tipW - 8));
                Canvas.SetTop(HoverTooltipPill, Math.Max(4, closestTopCenter.Y - tipH - 12));
            }
            else
            {
                HoverTooltipPill.Visibility = Visibility.Collapsed;
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            _isPointerInside = true;
            _pointerX = e.GetPosition(this).X;
            if (_isAutoHidden)
            {
                _isAutoHidden = false;
                _autoHideTargetY = 0;
            }
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            _isPointerInside = false;
            HoverTooltipPill.Visibility = Visibility.Collapsed;
        }

        private void OnStatusTick(object? sender, EventArgs e)
        {
            if (!_configStore.Current.DockEnabled)
            {
                Visibility = Visibility.Hidden;
                return;
            }

            Visibility = Visibility.Visible;
            UpdateRunningIndicators();

            // Smart Auto-Hide when a window is maximized or fullscreen
            if (_configStore.Current.DockAutoHide && !DownloadsStackPopup.IsOpen)
            {
                bool shouldHide = Win32Shell.IsMaximizedOrFullscreenAppActive() && !_isPointerInside;

                // Reveal dock if cursor touches the bottom 8px edge of primary monitor
                if (shouldHide && Win32Shell.GetCursorPos(out var pt))
                {
                    var src = PresentationSource.FromVisual(this);
                    double dpiY = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                    double screenBottomPx = SystemParameters.PrimaryScreenHeight * dpiY;
                    if (pt.Y >= screenBottomPx - 8)
                        shouldHide = false;
                }

                _isAutoHidden = shouldHide;
                _autoHideTargetY = shouldHide ? 130 : 0;
            }
            else
            {
                _isAutoHidden = false;
                _autoHideTargetY = 0;
            }
        }

        private void UpdateRunningIndicators()
        {
            HashSet<string> running;
            try
            {
                running = new HashSet<string>(
                    Process.GetProcesses().Select(p => p.ProcessName),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return;
            }

            foreach (var slot in _slots)
            {
                if (!slot.IsPinnedApp || string.IsNullOrWhiteSpace(slot.ProcessName))
                {
                    slot.RunningDot.Visibility = Visibility.Hidden;
                    continue;
                }

                bool isRunning = running.Contains(slot.ProcessName);
                slot.RunningDot.Visibility = isRunning ? Visibility.Visible : Visibility.Hidden;
            }
        }

        #endregion

        #region Drag-and-Drop Pinning, Context Menus & Downloads Stack

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped) return;

            bool added = false;
            foreach (string path in dropped)
            {
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".exe" && ext != ".lnk") continue;

                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (_configStore.Current.DockPins.Any(p => string.Equals(p.TargetPath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _configStore.Current.DockPins.Add(new DockPinItem
                {
                    Name = name,
                    TargetPath = path,
                    ProcessName = name,
                    FallbackEmoji = "🚀"
                });
                added = true;
            }

            if (added)
            {
                _configStore.Save();
                RebuildDockItems();
            }
        }

        private ContextMenu BuildPinnedAppContextMenu(DockPinItem pin)
        {
            var menu = new ContextMenu();

            var openNew = new MenuItem { Header = LocaleManager.Get("DockOpenNew", "Open New Instance") };
            openNew.Click += (_, _) => LaunchTarget(pin.TargetPath);
            menu.Items.Add(openNew);

            var unpin = new MenuItem { Header = LocaleManager.Get("DockUnpin", "Unpin from Dock") };
            unpin.Click += (_, _) =>
            {
                _configStore.Update(c => c.DockPins.RemoveAll(p => p.Id == pin.Id));
                RebuildDockItems();
            };
            menu.Items.Add(unpin);

            return menu;
        }

        private ContextMenu BuildDownloadsContextMenu()
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = LocaleManager.Get("DockOpenDownloads", "Open Downloads Folder") };
            open.Click += (_, _) => LaunchTarget(GetDownloadsPath());
            menu.Items.Add(open);
            return menu;
        }

        private ContextMenu BuildRecycleBinContextMenu()
        {
            var menu = new ContextMenu();
            var empty = new MenuItem { Header = LocaleManager.Get("DockEmptyBin", "Empty Recycle Bin") };
            empty.Click += (_, _) => Win32Shell.EmptyRecycleBin();
            menu.Items.Add(empty);
            return menu;
        }

        private void ToggleDownloadsStack()
        {
            if (DownloadsStackPopup.IsOpen)
            {
                DownloadsStackPopup.IsOpen = false;
                return;
            }

            DownloadsListPanel.Children.Clear();
            string dlPath = GetDownloadsPath();
            try
            {
                var recent = new DirectoryInfo(dlPath)
                    .GetFiles()
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(6)
                    .ToList();

                if (recent.Count == 0)
                {
                    DownloadsListPanel.Children.Add(new TextBlock
                    {
                        Text = "Downloads folder is empty",
                        FontSize = 11.5,
                        Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                        Margin = new Thickness(4)
                    });
                }
                else
                {
                    foreach (var file in recent)
                    {
                        string fullPath = file.FullName;
                        var btn = new Button
                        {
                            Style = (Style)FindResource("IslandChipBtn"),
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Padding = new Thickness(8, 5, 8, 5),
                            Margin = new Thickness(0, 0, 0, 4),
                            Content = file.Name.Length > 30 ? file.Name.Substring(0, 27) + "…" : file.Name,
                            ToolTip = fullPath
                        };
                        btn.Click += (_, _) =>
                        {
                            DownloadsStackPopup.IsOpen = false;
                            LaunchTarget(fullPath);
                        };
                        DownloadsListPanel.Children.Add(btn);
                    }
                }
            }
            catch
            {
                LaunchTarget(dlPath);
                return;
            }

            DownloadsStackPopup.IsOpen = true;
        }

        private void OpenDownloadsFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            DownloadsStackPopup.IsOpen = false;
            LaunchTarget(GetDownloadsPath());
        }

        private static string GetDownloadsPath()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private static void LaunchTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch { }
        }

        #endregion
    }
}
