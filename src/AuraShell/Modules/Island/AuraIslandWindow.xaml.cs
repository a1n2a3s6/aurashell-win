using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AuraShell.Localization;
using AuraShell.Modules.Dock;
using AuraShell.Modules.Widgets;
using AuraShell.Native;
using AuraShell.Services;
using AuraShell.Views;

namespace AuraShell.Modules.Island
{
    public partial class AuraIslandWindow : Window
    {
        private const int HOTKEY_TOGGLE_ISLAND = 9001;
        private const int HOTKEY_OPEN_STUDIO = 9002;
        private const int HOTKEY_TOGGLE_LANG = 9003;

        private const double CompactWidth = 244;
        private const double CompactHeight = 36;
        private const double CompactRadius = 18;

        private const double ExpandedWidth = 548;
        private const double ExpandedHeight = 304;
        private const double ExpandedRadius = 26;

        private double _curW = CompactWidth, _velW;
        private double _curH = CompactHeight, _velH;
        private double _curR = CompactRadius, _velR;
        private double _curExpandT, _velExpandT;

        private double _targetW = CompactWidth;
        private double _targetH = CompactHeight;
        private double _targetR = CompactRadius;
        private double _targetExpandT;

        private readonly Stopwatch _frameClock = Stopwatch.StartNew();
        private double _lastFrameSec;
        private double _miniVisPhase;

        private readonly ConfigStore _configStore;
        private readonly MediaSessionService _mediaService;
        private readonly SystemTelemetry _telemetry;
        private readonly WeatherService _weatherService;
        private readonly AiCopilotService _aiService;
        private readonly WidgetManager? _widgetManager;
        private AeroDockWindow? _dockWindow;

        private readonly DispatcherTimer _collapseDelayTimer;
        private readonly DispatcherTimer _systemPollTimer;
        private readonly DispatcherTimer _toastTimer;
        private readonly Button[] _tabButtons;
        private readonly UIElement[] _tabViews;
        private readonly Rectangle[] _miniBars;

        private HwndSource? _hwndSource;
        private StudioSettingsWindow? _studioWindow;

        private bool _isExpanded;
        private bool _isPinnedExpanded;
        private bool _hiddenForFullscreen;
        private bool _hiddenForCapture;
        private bool _isMediaPlaying;
        private int _activeTabIndex;

        public AuraIslandWindow(
            ConfigStore configStore,
            MediaSessionService mediaService,
            SystemTelemetry telemetry,
            WeatherService weatherService,
            AiCopilotService aiService,
            WidgetManager? widgetManager = null)
        {
            InitializeComponent();

            _configStore = configStore;
            _mediaService = mediaService;
            _telemetry = telemetry;
            _weatherService = weatherService;
            _aiService = aiService;
            _widgetManager = widgetManager;

            _tabButtons = new[] { TabBtn0, TabBtn1, TabBtn2, TabBtn3, TabBtn4 };
            _tabViews = new UIElement[] { MediaTab, AiTab, ShelfTab, ProdTab, UtilTab };
            _miniBars = new[] { MiniBar0, MiniBar1, MiniBar2, MiniBar3 };

            FlowDirection = LocaleManager.CurrentFlowDirection;
            PositionTopCenter();

            MediaTab.Initialize(_mediaService);
            AiTab.Initialize(_aiService, SetTemporarilyVisible, ShowToast);
            ShelfTab.Initialize(_configStore, ShowToast);
            ProdTab.Initialize(_configStore, ShowToast);
            UtilTab.Initialize(_configStore, _weatherService, SetTemporarilyVisible, ShowToast, OpenStudio);

            _mediaService.MediaChanged += (_, snap) => Dispatcher.Invoke(() => OnMediaChanged(snap));
            LocaleManager.LocaleChanged += (_, _) => Dispatcher.Invoke(UpdateLangButtonText);
            UpdateLangButtonText();
            SelectTab(0);

            _collapseDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(360) };
            _collapseDelayTimer.Tick += (_, _) =>
            {
                _collapseDelayTimer.Stop();
                if (!PillBorder.IsMouseOver && !_isPinnedExpanded && !IsAnyTabEditing())
                    SetExpanded(false);
            };

            _systemPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _systemPollTimer.Tick += OnSystemPollTick;
            _systemPollTimer.Start();

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                ToastBanner.Visibility = Visibility.Collapsed;
            };

            CompositionTarget.Rendering += OnSpringFrame;
        }

        public void AttachDock(AeroDockWindow dockWindow) => _dockWindow = dockWindow;

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            Win32Shell.HideFromAltTab(this);
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(WndProc);
                Win32Shell.AddClipboardFormatListener(_hwndSource.Handle);
                uint mods = Win32Shell.MOD_CONTROL | Win32Shell.MOD_ALT | Win32Shell.MOD_NOREPEAT;
                Win32Shell.RegisterHotKey(_hwndSource.Handle, HOTKEY_TOGGLE_ISLAND, mods, 0x41); // Ctrl+Alt+A
                Win32Shell.RegisterHotKey(_hwndSource.Handle, HOTKEY_OPEN_STUDIO, mods, 0x53);   // Ctrl+Alt+S
                Win32Shell.RegisterHotKey(_hwndSource.Handle, HOTKEY_TOGGLE_LANG, mods, 0x4C);   // Ctrl+Alt+L
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            CompositionTarget.Rendering -= OnSpringFrame;
            _systemPollTimer.Stop();
            _collapseDelayTimer.Stop();
            _toastTimer.Stop();

            if (_hwndSource != null)
            {
                UnregisterHotkeys();
                Win32Shell.RemoveClipboardFormatListener(_hwndSource.Handle);
                _hwndSource.RemoveHook(WndProc);
            }
            base.OnClosed(e);
        }

        public void UnregisterHotkeys()
        {
            if (_hwndSource == null) return;
            try
            {
                Win32Shell.UnregisterHotKey(_hwndSource.Handle, HOTKEY_TOGGLE_ISLAND);
                Win32Shell.UnregisterHotKey(_hwndSource.Handle, HOTKEY_OPEN_STUDIO);
                Win32Shell.UnregisterHotKey(_hwndSource.Handle, HOTKEY_TOGGLE_LANG);
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Shell.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_TOGGLE_ISLAND)
                {
                    if (Visibility != Visibility.Visible) { _configStore.Update(c => c.IslandEnabled = true); Visibility = Visibility.Visible; }
                    SetExpanded(!_isExpanded);
                    handled = true;
                }
                else if (id == HOTKEY_OPEN_STUDIO)
                {
                    OpenStudio();
                    handled = true;
                }
                else if (id == HOTKEY_TOGGLE_LANG)
                {
                    LocaleManager.ToggleLanguage();
                    _configStore.Update(c => c.Language = LocaleManager.CurrentLanguage);
                    UpdateLangButtonText();
                    handled = true;
                }
            }

            if (msg == Win32Shell.WM_CLIPBOARDUPDATE)
            {
                try
                {
                    if (Clipboard.ContainsText())
                        ShelfTab.RecordClipboardText(Clipboard.GetText());
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        public void PositionTopCenter()
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2.0;
            Top = 0;
        }

        public void OpenStudio()
        {
            if (_studioWindow != null && _studioWindow.IsLoaded)
            {
                _studioWindow.Activate();
                return;
            }

            _studioWindow = new StudioSettingsWindow(
                _configStore, _weatherService, _aiService, this, _widgetManager, _dockWindow, () =>
                {
                    AiTab.UpdateProviderLabel();
                    _ = UtilTab.RefreshWeatherAsync();
                    UpdateLangButtonText();
                });
            _studioWindow.Closed += (_, _) => _studioWindow = null;
            _studioWindow.Show();
        }

        private void SetTemporarilyVisible(bool visible)
        {
            _hiddenForCapture = !visible;
            UpdateEffectiveVisibility();
        }

        private void UpdateEffectiveVisibility()
        {
            bool shouldShow = _configStore.Current.IslandEnabled && !_hiddenForFullscreen && !_hiddenForCapture;
            Visibility = shouldShow ? Visibility.Visible : Visibility.Hidden;
        }

        #region 60fps Spring Physics Engine

        private void SetExpanded(bool expand)
        {
            if (_isExpanded == expand) return;
            _isExpanded = expand;

            if (expand)
            {
                _targetW = ExpandedWidth;
                _targetH = ExpandedHeight;
                _targetR = ExpandedRadius;
                _targetExpandT = 1.0;
                ExpandedView.Visibility = Visibility.Visible;
                MediaTab.SyncAudioState();
            }
            else
            {
                _targetW = CompactWidth;
                _targetH = CompactHeight;
                _targetR = CompactRadius;
                _targetExpandT = 0.0;
            }
        }

        private void OnSpringFrame(object? sender, EventArgs e)
        {
            double now = _frameClock.Elapsed.TotalSeconds;
            double dt = Math.Clamp(now - _lastFrameSec, 0.001, 0.033);
            _lastFrameSec = now;

            const double stiffness = 260.0;
            const double damping = 22.0;

            StepSpring(ref _curW, ref _velW, _targetW, stiffness, damping, dt);
            StepSpring(ref _curH, ref _velH, _targetH, stiffness, damping, dt);
            StepSpring(ref _curR, ref _velR, _targetR, stiffness, damping, dt);
            StepSpring(ref _curExpandT, ref _velExpandT, _targetExpandT, 320.0, 28.0, dt);

            PillBorder.Width = Math.Max(140, _curW);
            PillBorder.Height = Math.Max(32, _curH);
            double clampedR = Math.Clamp(_curR, 12, PillBorder.Height / 2.0);
            PillBorder.CornerRadius = new CornerRadius(clampedR);

            double t = Math.Clamp(_curExpandT, 0.0, 1.0);
            CompactView.Opacity = Math.Clamp(1.0 - t * 2.2, 0.0, 1.0);
            CompactView.Visibility = CompactView.Opacity > 0.02 ? Visibility.Visible : Visibility.Collapsed;

            ExpandedView.Opacity = Math.Clamp((t - 0.25) / 0.75, 0.0, 1.0);
            if (!_isExpanded && ExpandedView.Opacity <= 0.02)
                ExpandedView.Visibility = Visibility.Collapsed;

            if (CompactView.Visibility == Visibility.Visible)
            {
                _miniVisPhase += _isMediaPlaying ? dt * 11.0 : dt * 2.2;
                for (int i = 0; i < _miniBars.Length; i++)
                {
                    double h = _isMediaPlaying
                        ? 4 + 11 * (0.5 + 0.5 * Math.Sin(_miniVisPhase * (1.0 + i * 0.22) + i * 1.1))
                        : 3 + 2 * (0.5 + 0.5 * Math.Sin(_miniVisPhase + i));
                    _miniBars[i].Height = h;
                }
            }
        }

        private static void StepSpring(ref double value, ref double velocity, double target, double stiffness, double damping, double dt)
        {
            double force = stiffness * (target - value) - damping * velocity;
            velocity += force * dt;
            value += velocity * dt;

            if (Math.Abs(target - value) < 0.05 && Math.Abs(velocity) < 0.05)
            {
                value = target;
                velocity = 0;
            }
        }

        #endregion

        #region Polling, Media, Tabs & Interaction

        private void OnSystemPollTick(object? sender, EventArgs e)
        {
            bool fullscreen = _configStore.Current.IslandHideInFullscreen && Win32Shell.IsFullscreenAppActive();
            if (fullscreen != _hiddenForFullscreen)
            {
                _hiddenForFullscreen = fullscreen;
                UpdateEffectiveVisibility();
            }

            if (_hiddenForFullscreen || !_configStore.Current.IslandEnabled) return;

            var snap = _telemetry.Sample();
            UtilTab.UpdateTelemetry(snap);

            if (ProdTab.IsTimerRunning)
            {
                CompactTimerBadge.Text = $"⏱ {ProdTab.ActiveTimerBadge}";
                CompactTimerBadge.Visibility = Visibility.Visible;
            }
            else
            {
                CompactTimerBadge.Visibility = Visibility.Collapsed;
            }

            string mode = _configStore.Current.IslandDefaultPillMode;
            if (string.Equals(mode, "Telemetry", StringComparison.OrdinalIgnoreCase))
            {
                CompactPrimaryText.Text = $"CPU {snap.CpuPercent}% · RAM {snap.RamPercent}%";
            }
            else if (!_isMediaPlaying || string.IsNullOrWhiteSpace(_mediaService.Current.Title))
            {
                CompactPrimaryText.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
        }

        private void OnMediaChanged(MediaSessionSnapshot snap)
        {
            _isMediaPlaying = snap.IsPlaying;

            if (snap.HasSession && snap.AlbumArt != null)
            {
                CompactAlbumArt.Source = snap.AlbumArt;
                CompactAlbumArt.Visibility = Visibility.Visible;
                CompactAuraDot.Visibility = Visibility.Collapsed;
            }
            else
            {
                CompactAlbumArt.Source = null;
                CompactAlbumArt.Visibility = Visibility.Collapsed;
                CompactAuraDot.Visibility = Visibility.Visible;
            }

            if (snap.IsPlaying && !string.IsNullOrWhiteSpace(snap.Title))
                CompactPrimaryText.Text = snap.Title;
            else
                CompactPrimaryText.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private void SelectTab(int index)
        {
            _activeTabIndex = Math.Clamp(index, 0, _tabViews.Length - 1);
            var activeBg = new SolidColorBrush(Color.FromRgb(25, 36, 56));
            var activeBorder = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            var idleBg = Brushes.Transparent;
            var idleBorder = Brushes.Transparent;

            for (int i = 0; i < _tabViews.Length; i++)
            {
                bool active = i == _activeTabIndex;
                _tabViews[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                _tabButtons[i].Background = active ? activeBg : idleBg;
                _tabButtons[i].BorderBrush = active ? activeBorder : idleBorder;
            }
        }

        private void TabBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int idx))
                SelectTab(idx);
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_isExpanded) return;
            if (e.Delta < 0)
                SelectTab((_activeTabIndex + 1) % _tabViews.Length);
            else if (e.Delta > 0)
                SelectTab((_activeTabIndex - 1 + _tabViews.Length) % _tabViews.Length);
        }

        private void PillBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            _collapseDelayTimer.Stop();
            if (!_configStore.Current.FocusDndEnabled || _isExpanded)
                SetExpanded(true);
        }

        private void PillBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isPinnedExpanded && !IsAnyTabEditing())
            {
                _collapseDelayTimer.Stop();
                _collapseDelayTimer.Start();
            }
        }

        private void PillBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isExpanded)
                SetExpanded(true);
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                SetExpanded(true);
                SelectTab(2);
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                SetExpanded(true);
                SelectTab(2);
                ShelfTab.AddFiles(files);
                ShowToast($"📂 +{files.Length}");
            }
        }

        private void QuickLangBtn_Click(object sender, RoutedEventArgs e)
        {
            LocaleManager.ToggleLanguage();
            _configStore.Update(c => c.Language = LocaleManager.CurrentLanguage);
            UpdateLangButtonText();
        }

        private void UpdateLangButtonText()
        {
            QuickLangBtn.Content = LocaleManager.IsArabic ? "EN" : "ع";
        }

        private void PinExpandBtn_Click(object sender, RoutedEventArgs e)
        {
            _isPinnedExpanded = !_isPinnedExpanded;
            PinExpandBtn.BorderBrush = _isPinnedExpanded
                ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                : new SolidColorBrush(Color.FromRgb(38, 46, 66));
            if (!_isPinnedExpanded && !PillBorder.IsMouseOver)
                SetExpanded(false);
        }

        private bool IsAnyTabEditing()
        {
            return AiTab.IsInputFocused || ShelfTab.IsEditingText || ProdTab.IsEditingText;
        }

        public void ShowToast(string message)
        {
            ToastText.Text = message;
            ToastBanner.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        #endregion
    }
}
