using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace AuraShell.Native
{
    public sealed class MediaSessionSnapshot
    {
        public string Title { get; init; } = string.Empty;
        public string Artist { get; init; } = string.Empty;
        public string AlbumTitle { get; init; } = string.Empty;
        public string SourceAppId { get; init; } = string.Empty;
        public bool IsPlaying { get; init; }
        public bool HasSession { get; init; }
        public ImageSource? AlbumArt { get; init; }
    }

    /// <summary>
    /// Real WinRT GlobalSystemMediaTransportControlsSessionManager wrapper pulling live
    /// Title, Artist, Album Art bitmap, and Play/Pause/Next/Prev for Spotify, YouTube, Chrome, Edge, etc.
    /// </summary>
    public sealed class MediaSessionService : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private bool _disposed;

        public MediaSessionSnapshot Current { get; private set; } = new();
        public event EventHandler<MediaSessionSnapshot>? MediaChanged;

        public async Task InitializeAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_manager == null) return;

                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                AttachToCurrentSession();
                await RefreshAsync();
            }
            catch
            {
                // SMTC may be unavailable in restricted sessions
            }
        }

        private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            AttachToCurrentSession();
            _ = RefreshAsync();
        }

        private void AttachToCurrentSession()
        {
            if (_currentSession != null)
            {
                try
                {
                    _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                }
                catch { }
            }

            _currentSession = _manager?.GetCurrentSession();
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            }
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            _ = RefreshAsync();
        }

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            _ = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            if (_disposed) return;
            await _refreshLock.WaitAsync();
            try
            {
                var session = _currentSession ?? _manager?.GetCurrentSession();
                if (session == null)
                {
                    Current = new MediaSessionSnapshot { HasSession = false };
                    MediaChanged?.Invoke(this, Current);
                    return;
                }

                var props = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                ImageSource? art = null;
                if (props?.Thumbnail != null)
                {
                    try
                    {
                        using var winrtStream = await props.Thumbnail.OpenReadAsync();
                        using var netStream = winrtStream.AsStreamForRead();
                        using var ms = new MemoryStream();
                        await netStream.CopyToAsync(ms);
                        ms.Position = 0;

                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 128;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        art = bmp;
                    }
                    catch
                    {
                        art = null;
                    }
                }

                Current = new MediaSessionSnapshot
                {
                    HasSession = true,
                    Title = props?.Title?.Trim() ?? string.Empty,
                    Artist = props?.Artist?.Trim() ?? string.Empty,
                    AlbumTitle = props?.AlbumTitle?.Trim() ?? string.Empty,
                    SourceAppId = session.SourceAppUserModelId ?? string.Empty,
                    IsPlaying = isPlaying,
                    AlbumArt = art
                };

                MediaChanged?.Invoke(this, Current);
            }
            catch
            {
                // Ignore transient COM/WinRT disconnect during app close
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public async Task TogglePlayPauseAsync()
        {
            try
            {
                if (_currentSession != null)
                {
                    await _currentSession.TryTogglePlayPauseAsync();
                    await RefreshAsync();
                    return;
                }
            }
            catch { }

            Win32Shell.SimulateMediaPlayPause();
        }

        public async Task NextTrackAsync()
        {
            try
            {
                if (_currentSession != null)
                {
                    await _currentSession.TrySkipNextAsync();
                    await RefreshAsync();
                    return;
                }
            }
            catch { }

            Win32Shell.SimulateMediaNext();
        }

        public async Task PreviousTrackAsync()
        {
            try
            {
                if (_currentSession != null)
                {
                    await _currentSession.TrySkipPreviousAsync();
                    await RefreshAsync();
                    return;
                }
            }
            catch { }

            Win32Shell.SimulateMediaPrevious();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_currentSession != null)
            {
                try
                {
                    _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                }
                catch { }
            }
            if (_manager != null)
            {
                try { _manager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch { }
            }
            _refreshLock.Dispose();
        }
    }
}
