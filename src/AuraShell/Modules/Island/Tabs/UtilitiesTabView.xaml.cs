using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AuraShell.Localization;
using AuraShell.Native;
using AuraShell.Services;
using WinForms = System.Windows.Forms;

namespace AuraShell.Modules.Island.Tabs
{
    public partial class UtilitiesTabView : UserControl
    {
        private ConfigStore? _configStore;
        private WeatherService? _weatherService;
        private Action<bool>? _setIslandVisible;
        private Action<string>? _showToast;
        private Action? _openStudio;
        private string _latestIp = "127.0.0.1";

        public UtilitiesTabView()
        {
            InitializeComponent();
        }

        public void Initialize(
            ConfigStore configStore,
            WeatherService weatherService,
            Action<bool> setIslandVisible,
            Action<string> showToast,
            Action openStudio)
        {
            _configStore = configStore;
            _weatherService = weatherService;
            _setIslandVisible = setIslandVisible;
            _showToast = showToast;
            _openStudio = openStudio;

            _ = RefreshWeatherAsync();
        }

        public void UpdateTelemetry(TelemetrySnapshot snap)
        {
            _latestIp = snap.LocalIp;
            CpuText.Text = $"{snap.CpuPercent}%";
            RamText.Text = $"{snap.RamPercent}%";
            BatteryText.Text = snap.HasBattery
                ? (snap.IsCharging ? $"⚡{snap.BatteryPercent}%" : $"{snap.BatteryPercent}%")
                : "AC ⚡";
            NetText.Text = $"↓{FormatRate(snap.NetDownKbps)} ↑{FormatRate(snap.NetUpKbps)}";
        }

        private static string FormatRate(double kbps)
        {
            return kbps >= 1024 ? $"{kbps / 1024.0:0.0}M" : $"{Math.Round(kbps)}K";
        }

        public async Task RefreshWeatherAsync()
        {
            if (_configStore == null || _weatherService == null) return;
            var cfg = _configStore.Current;
            WeatherCityText.Text = cfg.WeatherCity;
            var snap = await _weatherService.FetchCurrentAsync(cfg.WeatherCity, cfg.WeatherLatitude, cfg.WeatherLongitude);
            if (snap != null)
            {
                WeatherCityText.Text = snap.City;
                WeatherTempText.Text = snap.DisplayShort;
                WeatherPillBorder.ToolTip = $"{snap.City}: {snap.LocalizedCondition} ({Math.Round(snap.TemperatureC)}°C)";
            }
        }

        private async void WeatherPill_Click(object sender, MouseButtonEventArgs e)
        {
            await RefreshWeatherAsync();
            _showToast?.Invoke("☀️ Weather updated");
        }

        private void EyedropperBtn_Click(object sender, RoutedEventArgs e)
        {
            _setIslandVisible?.Invoke(false);
            var overlay = new EyedropperOverlayWindow();
            overlay.ColorPicked += (_, hex) =>
            {
                try { Clipboard.SetText(hex); } catch { }
                _setIslandVisible?.Invoke(true);
                _showToast?.Invoke($"🎨 {hex} copied!");
            };
            overlay.Closed += (_, _) => _setIslandVisible?.Invoke(true);
            overlay.Show();
        }

        private void RegionShotBtn_Click(object sender, RoutedEventArgs e)
        {
            _setIslandVisible?.Invoke(false);
            var overlay = new RegionScreenshotWindow();
            overlay.RegionCaptured += (_, savedPath) =>
            {
                _setIslandVisible?.Invoke(true);
                _showToast?.Invoke(LocaleManager.Get("UtilShotSaved", "Screenshot copied & saved"));
            };
            overlay.Closed += (_, _) => _setIslandVisible?.Invoke(true);
            overlay.Show();
        }

        private async void FullShotBtn_Click(object sender, RoutedEventArgs e)
        {
            _setIslandVisible?.Invoke(false);
            await Task.Delay(180);
            try
            {
                var bounds = WinForms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }

                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AuraShell");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"AuraShot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                bmp.Save(file, ImageFormat.Png);

                IntPtr hBitmap = bmp.GetHbitmap();
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    Clipboard.SetImage(src);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }

                _showToast?.Invoke(LocaleManager.Get("UtilShotSaved", "Screenshot copied & saved"));
            }
            catch (Exception ex)
            {
                _showToast?.Invoke(ex.Message);
            }
            finally
            {
                _setIslandVisible?.Invoke(true);
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private void LockPcBtn_Click(object sender, RoutedEventArgs e)
        {
            Win32Shell.LockWorkStation();
        }

        private void EmptyBinBtn_Click(object sender, RoutedEventArgs e)
        {
            Win32Shell.EmptyRecycleBin();
            _showToast?.Invoke(LocaleManager.Get("UtilBinEmptied", "Recycle Bin emptied"));
        }

        private void CopyIpBtn_Click(object sender, RoutedEventArgs e)
        {
            string ip = string.IsNullOrEmpty(_latestIp) ? SystemTelemetry.GetLocalIpAddress() : _latestIp;
            try
            {
                Clipboard.SetText(ip);
                _showToast?.Invoke($"🌐 {ip}");
            }
            catch { }
        }

        private void QrBtn_Click(object sender, RoutedEventArgs e)
        {
            if (QrPreviewBorder.Visibility == Visibility.Visible)
            {
                QrPreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }

            string text = string.Empty;
            try
            {
                if (Clipboard.ContainsText()) text = Clipboard.GetText().Trim();
            }
            catch { }

            if (string.IsNullOrWhiteSpace(text))
                text = $"http://{_latestIp}";

            QrImage.Source = QrCodeGenerator.GenerateBitmap(text, 5);
            QrCaption.Text = text.Length > 24 ? text.Substring(0, 22) + "…" : text;
            QrPreviewBorder.Visibility = Visibility.Visible;
        }

        private void QrPreviewBorder_Click(object sender, MouseButtonEventArgs e)
        {
            QrPreviewBorder.Visibility = Visibility.Collapsed;
        }

        private void StudioBtn_Click(object sender, RoutedEventArgs e)
        {
            _openStudio?.Invoke();
        }
    }
}
