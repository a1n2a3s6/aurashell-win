using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuraShell.Localization;
using AuraShell.Native;

namespace AuraShell.Modules.Widgets.Cards
{
    public partial class AppleMusicWidget : UserControl, IAppleWidgetCard
    {
        private readonly MediaSessionService _mediaService;

        public string WidgetId => "music";
        public bool UsesCustomSkyBackdrop => false;

        public AppleMusicWidget(MediaSessionService mediaService)
        {
            InitializeComponent();
            _mediaService = mediaService;
            _mediaService.MediaChanged += (_, snap) => Dispatcher.Invoke(() => ApplySnapshot(snap));
            ApplySnapshot(_mediaService.Current);
        }

        private void ApplySnapshot(MediaSessionSnapshot snap)
        {
            if (snap.HasSession && !string.IsNullOrWhiteSpace(snap.Title))
            {
                TrackTitleText.Text = snap.Title;
                TrackArtistText.Text = string.IsNullOrWhiteSpace(snap.Artist)
                    ? snap.SourceAppId
                    : snap.Artist;
            }
            else
            {
                TrackTitleText.Text = LocaleManager.Get("MediaNothingPlaying", "Nothing Playing");
                TrackArtistText.Text = LocaleManager.Get("MediaIdleSubtitle", "Play Spotify, YouTube, Chrome or Edge");
            }

            PlayPauseGlyph.Text = snap.IsPlaying ? "⏸" : "▶";

            if (snap.AlbumArt != null)
            {
                AlbumArtImage.Source = snap.AlbumArt;
                AlbumArtImage.Visibility = Visibility.Visible;
                ArtPlaceholder.Visibility = Visibility.Collapsed;
                UpdateAmbientGlowFromArt(snap.AlbumArt);
            }
            else
            {
                AlbumArtImage.Source = null;
                AlbumArtImage.Visibility = Visibility.Collapsed;
                ArtPlaceholder.Visibility = Visibility.Visible;
                GlowColorStop.Color = Color.FromRgb(0xFF, 0x3B, 0x30);
            }
        }

        private void UpdateAmbientGlowFromArt(ImageSource source)
        {
            try
            {
                if (source is BitmapSource bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
                {
                    var formatted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                    int stride = formatted.PixelWidth * 4;
                    byte[] pixels = new byte[stride * formatted.PixelHeight];
                    formatted.CopyPixels(pixels, stride, 0);

                    long r = 0, g = 0, b = 0, count = 0;
                    int step = Math.Max(1, (pixels.Length / 4) / 256);
                    for (int i = 0; i < pixels.Length - 3; i += step * 4)
                    {
                        b += pixels[i];
                        g += pixels[i + 1];
                        r += pixels[i + 2];
                        count++;
                    }
                    if (count > 0)
                    {
                        GlowColorStop.Color = Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
                    }
                }
            }
            catch
            {
                GlowColorStop.Color = Color.FromRgb(0xFF, 0x3B, 0x30);
            }
        }

        public void ApplySize(AppleWidgetSize size)
        {
            bool compact = size == AppleWidgetSize.Small;
            DetailsGrid.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            SpacerCol.Width = compact ? new GridLength(0) : new GridLength(16);
            DetailsCol.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            ArtFrame.Width = compact ? 136 : 120;
            ArtFrame.Height = compact ? 136 : 120;
        }

        public void ApplyTheme(bool isDark)
        {
            TrackTitleText.Foreground = isDark
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(28, 28, 30));

            var btnBg = isDark
                ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
            var btnFg = isDark
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(28, 28, 30));

            PrevBtn.Background = btnBg;
            NextBtn.Background = btnBg;
            PrevBtn.Foreground = btnFg;
            NextBtn.Foreground = btnFg;
        }

        private async void PrevBtn_Click(object sender, RoutedEventArgs e) => await _mediaService.PreviousTrackAsync();
        private async void PlayPauseBtn_Click(object sender, RoutedEventArgs e) => await _mediaService.TogglePlayPauseAsync();
        private async void NextBtn_Click(object sender, RoutedEventArgs e) => await _mediaService.NextTrackAsync();
    }
}
