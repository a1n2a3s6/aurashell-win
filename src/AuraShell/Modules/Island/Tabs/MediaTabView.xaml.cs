using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AuraShell.Localization;
using AuraShell.Native;

namespace AuraShell.Modules.Island.Tabs
{
    public partial class MediaTabView : UserControl
    {
        private MediaSessionService? _mediaService;
        private readonly Rectangle[] _visBars;
        private readonly double[] _barPhases = { 0.0, 0.9, 1.8, 2.7, 3.6, 4.5, 5.4 };
        private readonly DispatcherTimer _animTimer;
        private double _phaseTime;
        private double _marqueeOffset;
        private bool _isPlaying;
        private bool _suppressVolumeEvent;

        public MediaTabView()
        {
            InitializeComponent();
            _visBars = new[] { VisBar0, VisBar1, VisBar2, VisBar3, VisBar4, VisBar5, VisBar6 };

            _animTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30fps smooth visualizer & marquee
            };
            _animTimer.Tick += OnAnimTick;
            _animTimer.Start();

            Loaded += (_, _) => SyncAudioState();
        }

        public void Initialize(MediaSessionService mediaService)
        {
            _mediaService = mediaService;
            _mediaService.MediaChanged += (_, snap) => Dispatcher.Invoke(() => ApplySnapshot(snap));
            ApplySnapshot(_mediaService.Current);
            SyncAudioState();
        }

        public void ApplySnapshot(MediaSessionSnapshot snap)
        {
            _isPlaying = snap.IsPlaying;

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

            PlayPauseBtn.Content = snap.IsPlaying ? "⏸" : "▶";
            _marqueeOffset = 0;
            Canvas.SetLeft(TrackTitleText, 0);

            if (snap.AlbumArt != null)
            {
                AlbumArtImage.Source = snap.AlbumArt;
                AlbumArtImage.Visibility = Visibility.Visible;
                ArtPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                AlbumArtImage.Source = null;
                AlbumArtImage.Visibility = Visibility.Collapsed;
                ArtPlaceholder.Visibility = Visibility.Visible;
            }
        }

        public void SyncAudioState()
        {
            try
            {
                _suppressVolumeEvent = true;
                int vol = (int)Math.Round(Win32Shell.GetMasterVolume() * 100f);
                VolumeSlider.Value = vol;
                VolumePctText.Text = $"{vol}%";

                bool muted = Win32Shell.GetMasterMute();
                MuteBtn.Content = muted || vol == 0 ? "🔇" : (vol < 40 ? "🔉" : "🔊");

                bool micMuted = Win32Shell.GetMicMute();
                UpdateMicVisual(micMuted);
            }
            finally
            {
                _suppressVolumeEvent = false;
            }
        }

        private void UpdateMicVisual(bool micMuted)
        {
            MicIcon.Text = micMuted ? "🚫" : "🎙️";
            MicLabel.Text = micMuted
                ? LocaleManager.Get("MediaMicMuted", "Mic Muted")
                : LocaleManager.Get("MediaMic", "Mic");
            MicMuteBtn.BorderBrush = micMuted
                ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                : new SolidColorBrush(Color.FromRgb(38, 46, 66));
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            if (Visibility != Visibility.Visible) return;

            _phaseTime += _isPlaying ? 0.24 : 0.04;
            for (int i = 0; i < _visBars.Length; i++)
            {
                double target = _isPlaying
                    ? 8 + 32 * (0.5 + 0.5 * Math.Sin(_phaseTime * (1.1 + i * 0.17) + _barPhases[i]))
                    : 5 + 3 * (0.5 + 0.5 * Math.Sin(_phaseTime + i));
                _visBars[i].Height = _visBars[i].Height * 0.65 + target * 0.35;
            }

            // Smooth horizontal marquee if title overflows container
            double hostW = TitleMarqueeHost.ActualWidth;
            double textW = TrackTitleText.ActualWidth;
            if (hostW > 20 && textW > hostW)
            {
                _marqueeOffset -= 0.7;
                if (_marqueeOffset < -(textW + 24))
                    _marqueeOffset = hostW;
                Canvas.SetLeft(TrackTitleText, _marqueeOffset);
            }
            else if (_marqueeOffset != 0)
            {
                _marqueeOffset = 0;
                Canvas.SetLeft(TrackTitleText, 0);
            }
        }

        private async void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaService != null) await _mediaService.PreviousTrackAsync();
        }

        private async void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaService != null) await _mediaService.TogglePlayPauseAsync();
        }

        private async void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaService != null) await _mediaService.NextTrackAsync();
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressVolumeEvent) return;
            int val = (int)Math.Round(e.NewValue);
            VolumePctText.Text = $"{val}%";
            Win32Shell.SetMasterVolume(val / 100f);
            MuteBtn.Content = val == 0 ? "🔇" : (val < 40 ? "🔉" : "🔊");
        }

        private void MuteBtn_Click(object sender, RoutedEventArgs e)
        {
            bool muted = Win32Shell.ToggleMasterMute();
            MuteBtn.Content = muted ? "🔇" : "🔊";
        }

        private void MicMuteBtn_Click(object sender, RoutedEventArgs e)
        {
            bool micMuted = Win32Shell.ToggleMicMute();
            UpdateMicVisual(micMuted);
        }

        private void AudioOutBtn_Click(object sender, RoutedEventArgs e)
        {
            Win32Shell.OpenSettingsUri("ms-settings:sound");
        }
    }
}
