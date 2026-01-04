using System;
using LibVLCSharp.Shared;
using Microsoft.UI.Dispatching;
using Photo.ViewModels;

namespace Photo.ViewModels
{
    public class MediaPlayerWrapper : ViewModelBase, IDisposable
    {
        private MediaPlayer? _mediaPlayer;
        private readonly DispatcherQueue _dispatcherQueue;
        private bool _isDisposed;
        public bool IgnorePropertyChanges { get; set; }

        public MediaPlayerWrapper(MediaPlayer mediaPlayer, DispatcherQueue dispatcherQueue)
        {
            _mediaPlayer = mediaPlayer;
            _dispatcherQueue = dispatcherQueue;
            
            if (_mediaPlayer != null)
            {
                _mediaPlayer.TimeChanged += OnTimeChanged;
                _mediaPlayer.LengthChanged += OnLengthChanged;
                _mediaPlayer.Playing += OnPlaying;
                _mediaPlayer.Paused += OnPaused;
                _mediaPlayer.Stopped += OnStopped;
                _mediaPlayer.VolumeChanged += OnVolumeChanged;
            }
        }

        public void RaiseAllPropertiesChanged()
        {
            OnPropertyChanged(nameof(TimeLong));
            OnPropertyChanged(nameof(TimeString));
            OnPropertyChanged(nameof(TotalTimeLong));
            OnPropertyChanged(nameof(TotalTimeString));
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(Volume));
        }

        private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            if (IgnorePropertyChanges) return;
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!_isDisposed)
                {
                    OnPropertyChanged(nameof(TimeLong));
                    OnPropertyChanged(nameof(TimeString));
                }
            });
        }

        private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
        {
            if (IgnorePropertyChanges) return;
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!_isDisposed)
                {
                    OnPropertyChanged(nameof(TotalTimeLong));
                    OnPropertyChanged(nameof(TotalTimeString));
                }
            });
        }

        private void OnPlaying(object? sender, EventArgs e) => UpdateIsPlaying();
        private void OnPaused(object? sender, EventArgs e) => UpdateIsPlaying();
        private void OnStopped(object? sender, EventArgs e) => UpdateIsPlaying();

        private void UpdateIsPlaying()
        {
            if (IgnorePropertyChanges) return;
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!_isDisposed)
                {
                    OnPropertyChanged(nameof(IsPlaying));
                }
            });
        }

        private void OnVolumeChanged(object? sender, MediaPlayerVolumeChangedEventArgs e)
        {
            if (IgnorePropertyChanges) return;
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!_isDisposed)
                {
                    OnPropertyChanged(nameof(Volume));
                }
            });
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_mediaPlayer != null)
            {
                _mediaPlayer.TimeChanged -= OnTimeChanged;
                _mediaPlayer.LengthChanged -= OnLengthChanged;
                _mediaPlayer.Playing -= OnPlaying;
                _mediaPlayer.Paused -= OnPaused;
                _mediaPlayer.Stopped -= OnStopped;
                _mediaPlayer.VolumeChanged -= OnVolumeChanged;
                _mediaPlayer = null;
            }
        }

        public long TimeLong
        {
            get => _mediaPlayer?.Time ?? 0;
            set
            {
                if (IgnorePropertyChanges) return;
                if (_mediaPlayer != null && _mediaPlayer.Time != value)
                {
                    _mediaPlayer.Time = value;
                    OnPropertyChanged();
                }
            }
        }

        public string TimeString => TimeSpan.FromMilliseconds(_mediaPlayer?.Time ?? 0).ToString(@"hh\:mm\:ss");

        public long TotalTimeLong => _mediaPlayer?.Length ?? 0;

        public string TotalTimeString => TimeSpan.FromMilliseconds(_mediaPlayer?.Length ?? 0).ToString(@"hh\:mm\:ss");

        public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;

        public int Volume
        {
            get => _mediaPlayer?.Volume ?? 0;
            set
            {
                if (_mediaPlayer != null && _mediaPlayer.Volume != value)
                {
                    _mediaPlayer.Volume = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
