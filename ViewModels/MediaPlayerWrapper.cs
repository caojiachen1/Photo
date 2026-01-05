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
        private int _lastVolume = 50; // 保存上次音量值，默认50
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
                _mediaPlayer.Muted += OnMuteChanged;
                _mediaPlayer.Unmuted += OnMuteChanged;
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
            OnPropertyChanged(nameof(IsMuted));
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
                    OnPropertyChanged(nameof(IsMuted));
                }
            });
        }

        private void OnMuteChanged(object? sender, EventArgs e)
        {
            if (IgnorePropertyChanges) return;
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!_isDisposed)
                {
                    OnPropertyChanged(nameof(IsMuted));
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
                _mediaPlayer.Muted -= OnMuteChanged;
                _mediaPlayer.Unmuted -= OnMuteChanged;
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
                    
                    // 如果音量被手动调到0，自动设置为静音
                    if (value == 0 && !_mediaPlayer.Mute)
                    {
                        _mediaPlayer.Mute = true;
                    }
                    // 如果音量从0调高，自动取消静音
                    else if (value > 0 && _mediaPlayer.Mute)
                    {
                        _mediaPlayer.Mute = false;
                    }
                    
                    // 保存非零音量值
                    if (value > 0)
                    {
                        _lastVolume = value;
                    }
                    
                    OnPropertyChanged();
                }
            }
        }

        public bool IsMuted => _mediaPlayer?.Mute ?? false;

        /// <summary>
        /// 切换静音状态，同时同步音量条
        /// </summary>
        public void ToggleMute()
        {
            if (_mediaPlayer == null) return;

            if (_mediaPlayer.Mute)
            {
                // 取消静音：恢复之前的音量
                _mediaPlayer.Mute = false;
                if (_lastVolume > 0)
                {
                    _mediaPlayer.Volume = _lastVolume;
                }
            }
            else
            {
                // 设置静音：保存当前音量并将音量条设为0
                if (_mediaPlayer.Volume > 0)
                {
                    _lastVolume = _mediaPlayer.Volume;
                }
                _mediaPlayer.Mute = true;
                _mediaPlayer.Volume = 0;
            }
        }
    }
}
