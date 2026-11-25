using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using AuraShell.Modules.Widgets.Cards;
using AuraShell.Native;
using AuraShell.Services;

namespace AuraShell.Modules.Widgets
{
    public sealed class WidgetManager : IDisposable
    {
        private readonly ConfigStore _configStore;
        private readonly MediaSessionService _mediaService;
        private readonly SystemTelemetry _telemetry;
        private readonly WeatherService _weatherService;

        private readonly List<AppleWidgetHostWindow> _hosts = new();
        private readonly DispatcherTimer _telemetryTimer;

        private AppleRingsWidget? _ringsCard;
        private AppleWeatherWidget? _weatherCard;
        private AppleMusicWidget? _musicCard;
        private AppleRemindersWidget? _remindersCard;
        private bool _disposed;

        public WidgetManager(
            ConfigStore configStore,
            MediaSessionService mediaService,
            SystemTelemetry telemetry,
            WeatherService weatherService)
        {
            _configStore = configStore;
            _mediaService = mediaService;
            _telemetry = telemetry;
            _weatherService = weatherService;

            _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _telemetryTimer.Tick += OnTelemetryTick;
        }

        public void InitializeAndShow()
        {
            double rightCol = Math.Max(32, SystemParameters.WorkArea.Right - 384);
            double rightHalf = rightCol + 188;

            var clockCard = new AppleClockWidget();
            var weatherCard = new AppleWeatherWidget(_weatherService);
            var musicCard = new AppleMusicWidget(_mediaService);
            var ringsCard = new AppleRingsWidget();
            var calCard = new AppleCalendarWidget();
            var remCard = new AppleRemindersWidget(_configStore);

            _weatherCard = weatherCard;
            _musicCard = musicCard;
            _ringsCard = ringsCard;
            _remindersCard = remCard;

            AddHost("clock", rightCol, 64, "Small", clockCard, clockCard);
            AddHost("weather", rightHalf, 64, "Small", weatherCard, weatherCard);
            AddHost("music", rightCol, 256, "Medium", musicCard, musicCard);
            AddHost("rings", rightCol, 448, "Medium", ringsCard, ringsCard);
            AddHost("calendar", 32, 64, "Small", calCard, calCard);
            AddHost("reminders", 32, 256, "Medium", remCard, remCard);

            _configStore.Save();
            _telemetryTimer.Start();

            OnTelemetryTick(null, EventArgs.Empty);
            var cfg = _configStore.Current;
            _ = _weatherService.FetchCurrentAsync(cfg.WeatherCity, cfg.WeatherLatitude, cfg.WeatherLongitude);
        }

        private void AddHost(string id, double defX, double defY, string defSize, System.Windows.Controls.UserControl control, IAppleWidgetCard card)
        {
            var placement = _configStore.GetOrCreateWidgetPlacement(id, defX, defY, defSize);
            var host = new AppleWidgetHostWindow(_configStore, placement, control, card);
            _hosts.Add(host);

            if (_configStore.Current.WidgetsEnabled && placement.IsVisible)
                host.Show();
        }

        private void OnTelemetryTick(object? sender, EventArgs e)
        {
            if (_disposed || !_configStore.Current.WidgetsEnabled) return;
            var snap = _telemetry.Sample();
            _ringsCard?.UpdateTelemetry(snap);
        }

        public void ApplyGlobalAppearance(string theme, double opacity)
        {
            _configStore.Update(c =>
            {
                c.WidgetTheme = theme;
                c.WidgetOpacity = opacity;
                foreach (var w in c.Widgets)
                {
                    w.Theme = theme;
                    w.Opacity = opacity;
                }
            });

            foreach (var host in _hosts)
                host.ApplyAppearance();
        }

        public void SetWidgetVisible(string widgetId, bool visible)
        {
            var host = _hosts.FirstOrDefault(h => string.Equals(h.WidgetId, widgetId, StringComparison.OrdinalIgnoreCase));
            if (host == null) return;

            host.Placement.IsVisible = visible;
            _configStore.Save();

            if (_configStore.Current.WidgetsEnabled && visible)
                host.Show();
            else
                host.Hide();
        }

        public void SetAllWidgetsLocked(bool locked)
        {
            foreach (var host in _hosts)
            {
                host.Placement.IsLocked = locked;
                host.ApplyAppearance();
            }
            _configStore.Save();
        }

        public void SetAllWidgetsEnabled(bool enabled)
        {
            _configStore.Update(c => c.WidgetsEnabled = enabled);
            foreach (var host in _hosts)
            {
                if (enabled && host.Placement.IsVisible)
                    host.Show();
                else
                    host.Hide();
            }
        }

        public void ToggleAllWidgets()
        {
            SetAllWidgetsEnabled(!_configStore.Current.WidgetsEnabled);
        }

        public void ResetLayout()
        {
            double rightCol = Math.Max(32, SystemParameters.WorkArea.Right - 384);
            double rightHalf = rightCol + 188;

            var defaults = new Dictionary<string, (double X, double Y, AppleWidgetSize Size)>
            {
                ["clock"] = (rightCol, 64, AppleWidgetSize.Small),
                ["weather"] = (rightHalf, 64, AppleWidgetSize.Small),
                ["music"] = (rightCol, 256, AppleWidgetSize.Medium),
                ["rings"] = (rightCol, 448, AppleWidgetSize.Medium),
                ["calendar"] = (32, 64, AppleWidgetSize.Small),
                ["reminders"] = (32, 256, AppleWidgetSize.Medium)
            };

            _configStore.Current.WidgetsEnabled = true;
            foreach (var host in _hosts)
            {
                if (defaults.TryGetValue(host.WidgetId, out var d))
                {
                    host.Placement.X = d.X;
                    host.Placement.Y = d.Y;
                    host.Placement.IsLocked = false;
                    host.Placement.IsVisible = true;
                    host.Left = d.X;
                    host.Top = d.Y;
                    host.ApplySize(d.Size, save: false);
                    host.ApplyAppearance();
                    host.Show();
                }
            }
            _configStore.Save();
        }

        public void RefreshReminders() => _remindersCard?.RefreshList();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _telemetryTimer.Stop();
            foreach (var host in _hosts)
            {
                try { host.Close(); } catch { }
            }
            _hosts.Clear();
        }
    }
}
