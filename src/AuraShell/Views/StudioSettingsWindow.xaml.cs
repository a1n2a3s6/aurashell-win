using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AuraShell.Localization;
using AuraShell.Modules.Dock;
using AuraShell.Modules.Island;
using AuraShell.Modules.Widgets;
using AuraShell.Native;
using AuraShell.Services;

namespace AuraShell.Views
{
    /// <summary>
    /// Bilingual Arabic (RTL) / English (LTR) macOS System Settings-inspired AuraShell Studio Control Center.
    /// </summary>
    public partial class StudioSettingsWindow : Window
    {
        private readonly ConfigStore _configStore;
        private readonly WeatherService _weatherService;
        private readonly AiCopilotService _aiService;
        private readonly AuraIslandWindow? _islandWindow;
        private readonly WidgetManager? _widgetManager;
        private readonly AeroDockWindow? _dockWindow;
        private readonly Action _onSettingsApplied;

        private readonly Button[] _navButtons;
        private readonly UIElement[] _pages;
        private readonly DispatcherTimer _memTimer;

        private string _selectedProvider;
        private string _selectedWidgetTheme;
        private string _selectedPillMode;
        private bool _initializing;

        public StudioSettingsWindow(
            ConfigStore configStore,
            WeatherService weatherService,
            AiCopilotService aiService,
            AuraIslandWindow? islandWindow,
            WidgetManager? widgetManager,
            AeroDockWindow? dockWindow,
            Action onSettingsApplied)
        {
            InitializeComponent();
            _initializing = true;

            _configStore = configStore;
            _weatherService = weatherService;
            _aiService = aiService;
            _islandWindow = islandWindow;
            _widgetManager = widgetManager;
            _dockWindow = dockWindow;
            _onSettingsApplied = onSettingsApplied;

            _navButtons = new[] { NavBtn0, NavBtn1, NavBtn2, NavBtn3, NavBtn4 };
            _pages = new UIElement[] { PageOverview, PageIsland, PageWidgets, PageDock, PageSystem };

            FlowDirection = LocaleManager.CurrentFlowDirection;

            var cfg = _configStore.Current;
            _selectedProvider = cfg.AiProvider;
            _selectedWidgetTheme = cfg.WidgetTheme;
            _selectedPillMode = cfg.IslandDefaultPillMode;

            // Populate Overview
            MasterIslandChk.IsChecked = cfg.IslandEnabled;
            MasterWidgetsChk.IsChecked = cfg.WidgetsEnabled;
            MasterDockChk.IsChecked = cfg.DockEnabled;

            // Populate AuraIsland
            IslandHideFsChk.IsChecked = cfg.IslandHideInFullscreen;
            GeminiKeyBox.Password = cfg.GeminiApiKey;
            GroqKeyBox.Password = cfg.GroqApiKey;
            IslandCaffeineChk.IsChecked = cfg.KeepAwakeEnabled;

            // Populate AuraWidgets
            SyncWidgetCheckboxes();
            int opPct = (int)Math.Clamp(Math.Round(cfg.WidgetOpacity * 100.0), 50, 100);
            WidgetOpacitySlider.Value = opPct;
            WidgetOpacityLabel.Text = $"{opPct}%";
            WidgetSnapChk.IsChecked = cfg.WidgetSnapToGrid;

            // Populate AeroDock
            DockSizeSlider.Value = Math.Clamp(cfg.DockIconSize, 36, 56);
            DockSizeLabel.Text = $"{Math.Round(DockSizeSlider.Value)} px";
            DockMagSlider.Value = Math.Clamp(cfg.DockMagnification, 1.0, 1.8);
            DockMagLabel.Text = $"{DockMagSlider.Value:0.0}x";
            DockAutoHideChk.IsChecked = cfg.DockAutoHide;
            HideTaskbarChk.IsChecked = cfg.HideWindowsTaskbar;

            // Populate System
            LaunchWindowsChk.IsChecked = Win32Shell.GetLaunchWithWindows();
            WeatherCityBox.Text = cfg.WeatherCity;

            UpdateHeaderLanguageButton();
            UpdateHighlights();
            SelectPage(0);

            _memTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _memTimer.Tick += (_, _) => UpdateMemoryReadout();
            _memTimer.Start();
            UpdateMemoryReadout();

            _initializing = false;
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            DwmGlass.EnableImmersiveDarkMode(this, true);
            DwmGlass.SetRoundedCorners(this, DwmGlass.CornerPreference.Round);
            DwmGlass.EnableAcrylicBlur(this, 0xCC, 0x0D, 0x11, 0x1A);
        }

        protected override void OnClosed(EventArgs e)
        {
            _memTimer.Stop();
            base.OnClosed(e);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void HeaderLangBtn_Click(object sender, RoutedEventArgs e)
        {
            LocaleManager.ToggleLanguage();
            _configStore.Update(c => c.Language = LocaleManager.CurrentLanguage);
            UpdateHeaderLanguageButton();
            _onSettingsApplied();
        }

        private void UpdateHeaderLanguageButton()
        {
            HeaderLangBtn.Content = LocaleManager.IsArabic ? "🌐 English (LTR)" : "🌐 العربية (RTL)";
        }

        private void NavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int idx))
                SelectPage(idx);
        }

        private void SelectPage(int idx)
        {
            var activeBg = new SolidColorBrush(Color.FromRgb(25, 38, 62));
            var activeBorder = new SolidColorBrush(Color.FromRgb(10, 132, 255));
            for (int i = 0; i < _pages.Length; i++)
            {
                bool active = i == idx;
                _pages[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                _navButtons[i].Background = active ? activeBg : Brushes.Transparent;
                _navButtons[i].BorderBrush = active ? activeBorder : Brushes.Transparent;
            }
        }

        #region Overview: Master Toggles & 1-Click Theme Presets

        private void MasterToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            bool islandOn = MasterIslandChk.IsChecked == true;
            bool widgetsOn = MasterWidgetsChk.IsChecked == true;
            bool dockOn = MasterDockChk.IsChecked == true;

            _configStore.Update(c =>
            {
                c.IslandEnabled = islandOn;
                c.WidgetsEnabled = widgetsOn;
                c.DockEnabled = dockOn;
            });

            if (_islandWindow != null)
                _islandWindow.Visibility = islandOn ? Visibility.Visible : Visibility.Hidden;
            _widgetManager?.SetAllWidgetsEnabled(widgetsOn);
            if (_dockWindow != null)
                _dockWindow.Visibility = dockOn ? Visibility.Visible : Visibility.Hidden;
        }

        private void PresetSonomaBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyPreset("SonomaDark", "Dark", 0.90);
        }

        private void PresetVisionBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyPreset("VisionLight", "Light", 0.92);
        }

        private void PresetObsidianBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyPreset("ObsidianPro", "Dark", 0.98);
        }

        private void ApplyPreset(string presetName, string widgetTheme, double opacity)
        {
            _selectedWidgetTheme = widgetTheme;
            _initializing = true;
            WidgetOpacitySlider.Value = Math.Round(opacity * 100);
            WidgetOpacityLabel.Text = $"{Math.Round(opacity * 100)}%";
            _initializing = false;

            _configStore.Update(c =>
            {
                c.ActivePreset = presetName;
                c.WidgetTheme = widgetTheme;
                c.WidgetOpacity = opacity;
            });

            _widgetManager?.ApplyGlobalAppearance(widgetTheme, opacity);
            _dockWindow?.ApplyTheme();
            UpdateHighlights();
            StatusText.Text = $"✓ {presetName}";
        }

        #endregion

        #region AuraIsland Tab

        private void PillModeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string mode)
            {
                _selectedPillMode = mode;
                _configStore.Update(c => c.IslandDefaultPillMode = mode);
                UpdateHighlights();
            }
        }

        private void ProvGeminiBtn_Click(object sender, RoutedEventArgs e)
        {
            _selectedProvider = "Gemini";
            UpdateHighlights();
        }

        private void ProvGroqBtn_Click(object sender, RoutedEventArgs e)
        {
            _selectedProvider = "Groq";
            UpdateHighlights();
        }

        private async void TestLatencyBtn_Click(object sender, RoutedEventArgs e)
        {
            TestLatencyBtn.IsEnabled = false;
            LatencyResultText.Text = "...";

            _configStore.Update(c =>
            {
                c.AiProvider = _selectedProvider;
                c.GeminiApiKey = GeminiKeyBox.Password.Trim();
                c.GroqApiKey = GroqKeyBox.Password.Trim();
            });

            var sw = Stopwatch.StartNew();
            string reply = await _aiService.AskTextAsync("Reply with one word: OK");
            sw.Stop();

            string preview = reply.Length > 50 ? reply.Substring(0, 48) + "…" : reply;
            LatencyResultText.Text = $"{_selectedProvider}: {sw.ElapsedMilliseconds} ms — {preview}";
            TestLatencyBtn.IsEnabled = true;
        }

        private void IslandCaffeineChk_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            bool on = IslandCaffeineChk.IsChecked == true;
            _configStore.Update(c => c.KeepAwakeEnabled = on);
            Win32Shell.SetKeepAwake(on);
        }

        #endregion

        #region AuraWidgets Tab

        private void SyncWidgetCheckboxes()
        {
            CheckBox[] boxes = { WidgetClockChk, WidgetMusicChk, WidgetRingsChk, WidgetWeatherChk, WidgetCalendarChk, WidgetRemindersChk };
            foreach (var cb in boxes)
            {
                string id = cb.Tag?.ToString() ?? string.Empty;
                var p = _configStore.Current.Widgets.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
                cb.IsChecked = p == null || p.IsVisible;
            }
        }

        private void WidgetGalleryChk_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            if (sender is CheckBox cb && cb.Tag is string id)
                _widgetManager?.SetWidgetVisible(id, cb.IsChecked == true);
        }

        private void ThemeDarkBtn_Click(object sender, RoutedEventArgs e) => ApplyWidgetThemeSelection("Dark");
        private void ThemeLightBtn_Click(object sender, RoutedEventArgs e) => ApplyWidgetThemeSelection("Light");
        private void ThemeAutoBtn_Click(object sender, RoutedEventArgs e) => ApplyWidgetThemeSelection("Auto");

        private void ApplyWidgetThemeSelection(string theme)
        {
            _selectedWidgetTheme = theme;
            UpdateHighlights();
            _widgetManager?.ApplyGlobalAppearance(_selectedWidgetTheme, WidgetOpacitySlider.Value / 100.0);
            _dockWindow?.ApplyTheme();
        }

        private void WidgetOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing || WidgetOpacityLabel == null) return;
            int pct = (int)Math.Round(e.NewValue);
            WidgetOpacityLabel.Text = $"{pct}%";
            _widgetManager?.ApplyGlobalAppearance(_selectedWidgetTheme, pct / 100.0);
        }

        private void WidgetSnapChk_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            _configStore.Update(c => c.WidgetSnapToGrid = WidgetSnapChk.IsChecked == true);
        }

        private void LockAllWidgetsBtn_Click(object sender, RoutedEventArgs e) => _widgetManager?.SetAllWidgetsLocked(true);
        private void UnlockAllWidgetsBtn_Click(object sender, RoutedEventArgs e) => _widgetManager?.SetAllWidgetsLocked(false);

        #endregion

        #region AeroDock Tab

        private void DockSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing || DockSizeLabel == null) return;
            double sz = Math.Round(e.NewValue);
            DockSizeLabel.Text = $"{sz} px";
            _configStore.Update(c => c.DockIconSize = sz);
        }

        private void DockMagSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_initializing || DockMagLabel == null) return;
            double mag = Math.Round(e.NewValue, 1);
            DockMagLabel.Text = $"{mag:0.0}x";
            _configStore.Update(c => c.DockMagnification = mag);
        }

        private void DockAutoHideChk_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            _configStore.Update(c => c.DockAutoHide = DockAutoHideChk.IsChecked == true);
        }

        private void HideTaskbarChk_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            bool hide = HideTaskbarChk.IsChecked == true;
            _configStore.Update(c => c.HideWindowsTaskbar = hide);
            Win32Shell.SetWindowsTaskbarVisible(!hide);
        }

        private void ResetDockBtn_Click(object sender, RoutedEventArgs e)
        {
            _configStore.Update(c => c.DockPins = ConfigStore.CreateDefaultDockPins());
            _dockWindow?.RebuildDockItems();
            StatusText.Text = "✓ AeroDock reset";
        }

        #endregion

        #region System & Startup Tab

        private void LaunchWindowsChk_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            Win32Shell.SetLaunchWithWindows(LaunchWindowsChk.IsChecked == true);
        }

        private void UpdateMemoryReadout()
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                double mb = proc.WorkingSet64 / (1024.0 * 1024.0);
                MemoryUsageText.Text = $"{mb:0.0} MB";
            }
            catch { }
        }

        private void TrimMemoryBtn_Click(object sender, RoutedEventArgs e)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            UpdateMemoryReadout();
        }

        private void ExitAppBtn_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        #endregion

        private void UpdateHighlights()
        {
            var activeBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            var idleBrush = new SolidColorBrush(Color.FromRgb(38, 46, 66));

            ThemeDarkBtn.BorderBrush = string.Equals(_selectedWidgetTheme, "Dark", StringComparison.OrdinalIgnoreCase) ? activeBrush : idleBrush;
            ThemeLightBtn.BorderBrush = string.Equals(_selectedWidgetTheme, "Light", StringComparison.OrdinalIgnoreCase) ? activeBrush : idleBrush;
            ThemeAutoBtn.BorderBrush = string.Equals(_selectedWidgetTheme, "Auto", StringComparison.OrdinalIgnoreCase) ? activeBrush : idleBrush;

            bool isGemini = string.Equals(_selectedProvider, "Gemini", StringComparison.OrdinalIgnoreCase);
            ProvGeminiBtn.BorderBrush = isGemini ? activeBrush : idleBrush;
            ProvGroqBtn.BorderBrush = !isGemini ? activeBrush : idleBrush;

            PillClockBtn.BorderBrush = string.Equals(_selectedPillMode, "Clock", StringComparison.OrdinalIgnoreCase) ? activeBrush : idleBrush;
            PillMediaBtn.BorderBrush = string.Equals(_selectedPillMode, "Media", StringComparison.OrdinalIgnoreCase) ? activeBrush : idleBrush;
            PillTelemetryBtn.BorderBrush = string.Equals(_selectedPillMode, "Telemetry", StringComparison.OrdinalIgnoreCase) ? activeBrush : idleBrush;
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveBtn.IsEnabled = false;
            string city = WeatherCityBox.Text.Trim();
            double lat = _configStore.Current.WeatherLatitude;
            double lon = _configStore.Current.WeatherLongitude;

            if (!string.IsNullOrWhiteSpace(city) &&
                !string.Equals(city, _configStore.Current.WeatherCity, StringComparison.OrdinalIgnoreCase))
            {
                var geo = await _weatherService.GeocodeCityAsync(city);
                if (geo.HasValue)
                {
                    city = geo.Value.City;
                    lat = geo.Value.Lat;
                    lon = geo.Value.Lon;
                }
            }

            _configStore.Update(c =>
            {
                c.IslandHideInFullscreen = IslandHideFsChk.IsChecked == true;
                c.IslandDefaultPillMode = _selectedPillMode;
                c.AiProvider = _selectedProvider;
                c.WidgetTheme = _selectedWidgetTheme;
                c.WidgetOpacity = WidgetOpacitySlider.Value / 100.0;
                c.GeminiApiKey = GeminiKeyBox.Password.Trim();
                c.GroqApiKey = GroqKeyBox.Password.Trim();
                if (!string.IsNullOrWhiteSpace(city))
                {
                    c.WeatherCity = city;
                    c.WeatherLatitude = lat;
                    c.WeatherLongitude = lon;
                }
            });

            _widgetManager?.ApplyGlobalAppearance(_selectedWidgetTheme, WidgetOpacitySlider.Value / 100.0);
            _dockWindow?.ApplyTheme();
            _onSettingsApplied();
            StatusText.Text = LocaleManager.Get("StudioSaved", "Settings saved!");
            SaveBtn.IsEnabled = true;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}
