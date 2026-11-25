using System;
using System.Drawing;
using System.Windows;
using AuraShell.Localization;
using AuraShell.Modules.Dock;
using AuraShell.Modules.Island;
using AuraShell.Modules.Widgets;
using AuraShell.Native;
using AuraShell.Services;
using WinForms = System.Windows.Forms;

namespace AuraShell
{
    public partial class App : Application
    {
        private ConfigStore? _configStore;
        private MediaSessionService? _mediaService;
        private SystemTelemetry? _telemetry;
        private WeatherService? _weatherService;
        private AiCopilotService? _aiService;
        private WidgetManager? _widgetManager;
        private AuraIslandWindow? _islandWindow;
        private AeroDockWindow? _dockWindow;
        private WinForms.NotifyIcon? _trayIcon;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Guarantee Windows Taskbar is always restored even on unexpected termination
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Win32Shell.SetWindowsTaskbarVisible(true);
            AppDomain.CurrentDomain.UnhandledException += (_, _) => Win32Shell.SetWindowsTaskbarVisible(true);
            DispatcherUnhandledException += (_, args) => { args.Handled = true; };

            _configStore = new ConfigStore();
            LocaleManager.ApplyLanguage(_configStore.Current.Language);

            _mediaService = new MediaSessionService();
            _telemetry = new SystemTelemetry();
            _weatherService = new WeatherService();
            _aiService = new AiCopilotService(_configStore);

            _widgetManager = new WidgetManager(_configStore, _mediaService, _telemetry, _weatherService);
            _widgetManager.InitializeAndShow();

            _islandWindow = new AuraIslandWindow(_configStore, _mediaService, _telemetry, _weatherService, _aiService, _widgetManager);
            if (_configStore.Current.IslandEnabled)
                _islandWindow.Show();

            _dockWindow = new AeroDockWindow(_configStore, () => _islandWindow.OpenStudio());
            _islandWindow.AttachDock(_dockWindow);
            if (_configStore.Current.DockEnabled)
                _dockWindow.Show();

            if (_configStore.Current.HideWindowsTaskbar)
                Win32Shell.SetWindowsTaskbarVisible(false);

            InitializeTrayIcon();
            LocaleManager.LocaleChanged += (_, _) => RebuildTrayMenu();

            await _mediaService.InitializeAsync();
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new WinForms.NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "AuraShell — Island, Widgets & AeroDock",
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => _islandWindow?.OpenStudio());
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (_trayIcon == null) return;

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add(LocaleManager.Get("TrayShowHide", "Toggle AuraIsland"), null, (_, _) => Dispatcher.Invoke(ToggleIslandVisibility));
            menu.Items.Add(LocaleManager.Get("TrayToggleWidgets", "Show / Hide Desktop Widgets"), null, (_, _) => Dispatcher.Invoke(() => _widgetManager?.ToggleAllWidgets()));
            menu.Items.Add(LocaleManager.Get("TrayToggleDock", "Show / Hide AeroDock"), null, (_, _) => Dispatcher.Invoke(ToggleDockVisibility));
            menu.Items.Add(LocaleManager.Get("TrayResetWidgets", "Reset Widget Layout"), null, (_, _) => Dispatcher.Invoke(() => _widgetManager?.ResetLayout()));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(LocaleManager.Get("TrayStudio", "Open AuraShell Studio"), null, (_, _) => Dispatcher.Invoke(() => _islandWindow?.OpenStudio()));
            menu.Items.Add(LocaleManager.Get("TraySwitchLang", "Switch Language"), null, (_, _) => Dispatcher.Invoke(() =>
            {
                LocaleManager.ToggleLanguage();
                _configStore?.Update(c => c.Language = LocaleManager.CurrentLanguage);
            }));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(LocaleManager.Get("TrayExit", "Exit AuraShell"), null, (_, _) => Dispatcher.Invoke(Shutdown));

            _trayIcon.ContextMenuStrip = menu;
        }

        private void ToggleIslandVisibility()
        {
            if (_islandWindow == null || _configStore == null) return;
            bool next = _islandWindow.Visibility != Visibility.Visible;
            _configStore.Update(c => c.IslandEnabled = next);
            _islandWindow.Visibility = next ? Visibility.Visible : Visibility.Hidden;
        }

        private void ToggleDockVisibility()
        {
            if (_dockWindow == null || _configStore == null) return;
            bool next = _dockWindow.Visibility != Visibility.Visible;
            _configStore.Update(c => c.DockEnabled = next);
            _dockWindow.Visibility = next ? Visibility.Visible : Visibility.Hidden;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Always safely restore Windows Taskbar & Sleep state on exit
            Win32Shell.SetWindowsTaskbarVisible(true);
            Win32Shell.SetKeepAwake(false);

            try { _islandWindow?.UnregisterHotkeys(); } catch { }
            try { _islandWindow?.Close(); } catch { }
            try { _dockWindow?.Close(); } catch { }
            _widgetManager?.Dispose();
            _mediaService?.Dispose();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            base.OnExit(e);
        }
    }
}
