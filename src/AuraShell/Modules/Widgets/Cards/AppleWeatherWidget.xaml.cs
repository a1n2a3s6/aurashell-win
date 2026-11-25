using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AuraShell.Localization;
using AuraShell.Services;

namespace AuraShell.Modules.Widgets.Cards
{
    public partial class AppleWeatherWidget : UserControl, IAppleWidgetCard
    {
        private readonly WeatherService _weatherService;
        private WeatherSnapshot? _lastSnapshot;

        public string WidgetId => "weather";
        public bool UsesCustomSkyBackdrop => true;

        public AppleWeatherWidget(WeatherService weatherService)
        {
            InitializeComponent();
            _weatherService = weatherService;
            _weatherService.WeatherUpdated += (_, snap) => Dispatcher.Invoke(() => ApplyWeather(snap));
            LocaleManager.LocaleChanged += (_, _) => Dispatcher.Invoke(() =>
            {
                if (_lastSnapshot != null) ApplyWeather(_lastSnapshot);
            });

            if (_weatherService.Latest != null)
                ApplyWeather(_weatherService.Latest);
        }

        public void ApplyWeather(WeatherSnapshot snap)
        {
            _lastSnapshot = snap;
            CityText.Text = snap.City;
            TempText.Text = $"{Math.Round(snap.TemperatureC)}°";
            ConditionIconText.Text = snap.Icon;
            ConditionLabelText.Text = snap.LocalizedCondition;

            string hLbl = LocaleManager.Get("WidgetWeatherHigh", "H");
            string lLbl = LocaleManager.Get("WidgetWeatherLow", "L");
            HighLowText.Text = $"{hLbl}:{Math.Round(snap.HighC)}°  {lLbl}:{Math.Round(snap.LowC)}°";

            ApplySkyGradient(snap.WeatherCode, snap.IsDay);
        }

        private void ApplySkyGradient(int code, bool isDay)
        {
            if (!isDay)
            {
                // Authentic iOS 18 clear night sky gradient
                SkyStopTop.Color = Color.FromRgb(0x0B, 0x10, 0x26);
                SkyStopMid.Color = Color.FromRgb(0x18, 0x24, 0x4A);
                SkyStopBot.Color = Color.FromRgb(0x2B, 0x3D, 0x70);
                return;
            }

            if (code >= 51) // Rain / Snow / Storm
            {
                SkyStopTop.Color = Color.FromRgb(0x3B, 0x4B, 0x5E);
                SkyStopMid.Color = Color.FromRgb(0x52, 0x65, 0x7A);
                SkyStopBot.Color = Color.FromRgb(0x6D, 0x81, 0x96);
            }
            else if (code >= 1) // Partly Cloudy / Fog
            {
                SkyStopTop.Color = Color.FromRgb(0x2E, 0x6F, 0xB8);
                SkyStopMid.Color = Color.FromRgb(0x56, 0x8F, 0xC9);
                SkyStopBot.Color = Color.FromRgb(0x88, 0xB4, 0xDE);
            }
            else // Sunny Clear Day
            {
                SkyStopTop.Color = Color.FromRgb(0x1B, 0x6F, 0xD8);
                SkyStopMid.Color = Color.FromRgb(0x3A, 0x8F, 0xE8);
                SkyStopBot.Color = Color.FromRgb(0x6C, 0xB8, 0xFF);
            }
        }

        public void ApplySize(AppleWidgetSize size)
        {
            TempText.FontSize = size == AppleWidgetSize.Large ? 56 : 44;
            CityText.MaxWidth = size == AppleWidgetSize.Small ? 124 : 280;
        }

        public void ApplyTheme(bool isDark)
        {
            // iOS 18 Weather always uses its dynamic sky gradient with crisp white text
        }
    }
}
