using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Photo.ViewModels
{
    public class VLCVideoPlayerViewModel : ViewModelBase, IDisposable
    {
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private MediaPlayerWrapper? _mediaPlayerWrapper;
        private Media? _currentMedia;
        private bool _loadPlayer = true;
        private Visibility _controlsVisibility = Visibility.Visible;
        private string _filePath = string.Empty;
        private int _rowSpan = 2;
        private bool _isNotFullScreen = true;
        private bool _isRepeat = false;
        private readonly DispatcherQueue _dispatcherQueue;

        private static readonly ConcurrentDictionary<string, long> _playbackHistory = new();
        private readonly SemaphoreSlim _playLock = new(1, 1);
        private string _currentPlayingPath = string.Empty;
        private bool _isDisposed;

        public VLCVideoPlayerViewModel(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;

            PlayPauseCommand = new RelayCommand(PlayPause);
            StopCommand = new RelayCommand(Stop);
            RewindCommand = new RelayCommand(Rewind);
            FastForwardCommand = new RelayCommand(FastForward);
            VolumeUpCommand = new RelayCommand(VolumeUp);
            VolumeDownCommand = new RelayCommand(VolumeDown);
            MuteCommand = new RelayCommand(Mute);
            RepeatCommand = new RelayCommand(ToggleRepeat);
            ToggleControlsCommand = new RelayCommand(ToggleControls);
            
            // Placeholder commands for events
            ScrollChangedCommand = new RelayCommand(() => { });
            InitializedCommand = new RelayCommand<InitializedEventArgs>(Initialize);
            PointerMovedCommand = new RelayCommand(() => { ControlsVisibility = Visibility.Visible; });
        }

        public void Initialize(InitializedEventArgs? args)
        {
            if (args == null) return;

            Core.Initialize();

            _libVLC = new LibVLC(enableDebugLogs: true, args.SwapChainOptions);
            _mediaPlayer = new MediaPlayer(_libVLC);
            _mediaPlayerWrapper = new MediaPlayerWrapper(_mediaPlayer, _dispatcherQueue);

            // 订阅播放结束事件以实现循环播放
            _mediaPlayer.EndReached += OnMediaEndReached;

            OnPropertyChanged(nameof(Player));
            OnPropertyChanged(nameof(MediaPlayerWrapper));

            if (!string.IsNullOrEmpty(_filePath))
            {
                PlayMedia(_filePath);
            }
        }

        public LibVLC? LibVLC => _libVLC;
        public MediaPlayer? Player => _mediaPlayer;
        public MediaPlayerWrapper? MediaPlayerWrapper => _mediaPlayerWrapper;

        public bool LoadPlayer
        {
            get => _loadPlayer;
            set => SetProperty(ref _loadPlayer, value);
        }

        public Visibility ControlsVisibility
        {
            get => _controlsVisibility;
            set => SetProperty(ref _controlsVisibility, value);
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (SetProperty(ref _filePath, value))
                {
                    PlayMedia(value);
                }
            }
        }

        public int RowSpan
        {
            get => _rowSpan;
            set => SetProperty(ref _rowSpan, value);
        }

        public bool IsNotFullScreen
        {
            get => _isNotFullScreen;
            set => SetProperty(ref _isNotFullScreen, value);
        }

        public bool IsRepeat
        {
            get => _isRepeat;
            set => SetProperty(ref _isRepeat, value);
        }

        public RelayCommand PlayPauseCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand RewindCommand { get; }
        public RelayCommand FastForwardCommand { get; }
        public RelayCommand VolumeUpCommand { get; }
        public RelayCommand VolumeDownCommand { get; }
        public RelayCommand MuteCommand { get; }
        public RelayCommand RepeatCommand { get; }
        public RelayCommand ToggleControlsCommand { get; }
        public RelayCommand ScrollChangedCommand { get; }
        public RelayCommand<InitializedEventArgs> InitializedCommand { get; }
        public RelayCommand PointerMovedCommand { get; }

        private async void PlayMedia(string path)
        {
            if (string.IsNullOrEmpty(path) || _libVLC == null || _mediaPlayer == null) return;
            
            SaveCurrentPosition();
            _currentPlayingPath = path;

            await Task.Run(async () => 
            {
                try 
                {
                    await _playLock.WaitAsync();
                    
                    if (_isDisposed || _mediaPlayer == null) return;

                    // 暂时禁用Wrapper的更新，防止UI回写旧的时间值
                    if (_mediaPlayerWrapper != null)
                    {
                        _mediaPlayerWrapper.IgnorePropertyChanges = true;
                    }

                    // 先停止并清理之前的播放
                    _mediaPlayer.Stop();
                    
                    // 释放旧的Media对象
                    _currentMedia?.Dispose();
                    _currentMedia = null;

                    if (_isDisposed || _mediaPlayer == null) return;

                    // 创建并播放新的Media
                    var media = new Media(_libVLC, path, FromType.FromPath);
                    
                    // 检查是否有历史播放记录
                    long startTime = 0;
                    if (_playbackHistory.TryGetValue(path, out long pos) && pos > 0)
                    {
                        startTime = pos;
                        // 使用选项设置开始时间，避免seek带来的卡顿
                        // 注意：LibVLC的时间选项通常是秒（浮点数）
                        media.AddOption($":start-time={startTime / 1000.0}");
                    }

                    _currentMedia = media;
                    _mediaPlayer.Play(_currentMedia);

                    // 恢复Wrapper的更新
                    if (_mediaPlayerWrapper != null)
                    {
                        // 稍微延迟一下，等待VLC状态稳定
                        await Task.Delay(100);
                        _mediaPlayerWrapper.IgnorePropertyChanges = false;
                        
                        // 强制通知一次UI更新，确保显示正确
                        _dispatcherQueue.TryEnqueue(() => 
                        {
                            _mediaPlayerWrapper.RaiseAllPropertiesChanged();
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Log error
                    System.Diagnostics.Debug.WriteLine($"Error playing media: {ex.Message}");
                    if (_mediaPlayerWrapper != null) _mediaPlayerWrapper.IgnorePropertyChanges = false;
                }
                finally
                {
                    _playLock.Release();
                }
            });
        }

        private void SaveCurrentPosition()
        {
            try
            {
                if (_mediaPlayer != null && !string.IsNullOrEmpty(_currentPlayingPath))
                {
                    var time = _mediaPlayer.Time;
                    var length = _mediaPlayer.Length;
                    
                    // 如果时间有效且不是在视频末尾（例如最后1秒内），则保存
                    if (time > 0 && (length == 0 || time < length - 1000))
                    {
                        _playbackHistory[_currentPlayingPath] = time;
                    }
                    else if (length > 0 && time >= length - 1000)
                    {
                        // 如果播放结束，重置为0
                        _playbackHistory[_currentPlayingPath] = 0;
                    }
                }
            }
            catch { }
        }

        private void PlayPause()
        {
            if (_mediaPlayer == null) return;
            if (_mediaPlayer.IsPlaying)
                _mediaPlayer.Pause();
            else
                _mediaPlayer.Play();
        }

        private void Stop()
        {
            _mediaPlayer?.Stop();
        }

        private void Rewind()
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Time -= 10000; // -10s
        }

        private void FastForward()
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Time += 10000; // +10s
        }

        private void VolumeUp()
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Volume = Math.Min(_mediaPlayer.Volume + 5, 100);
        }

        private void VolumeDown()
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Volume = Math.Max(_mediaPlayer.Volume - 5, 0);
        }

        private void Mute()
        {
            _mediaPlayerWrapper?.ToggleMute();
        }

        private void ToggleRepeat()
        {
            IsRepeat = !IsRepeat;
        }

        private void OnMediaEndReached(object? sender, EventArgs e)
        {
            if (IsRepeat && _mediaPlayer != null && _currentMedia != null)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    _mediaPlayer.Stop();
                    _mediaPlayer.Play(_currentMedia);
                });
            }
        }

        private void ToggleControls()
        {
            ControlsVisibility = ControlsVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            SaveCurrentPosition();

            if (_mediaPlayer != null)
            {
                _mediaPlayer.EndReached -= OnMediaEndReached;
            }

            _mediaPlayerWrapper?.Dispose();
            _mediaPlayerWrapper = null;

            var player = _mediaPlayer;
            var media = _currentMedia;
            var vlc = _libVLC;

            _mediaPlayer = null;
            _currentMedia = null;
            _libVLC = null;

            // 在后台线程释放资源，避免阻塞UI线程导致卡顿
            Task.Run(() => 
            {
                try 
                {
                    _playLock.Wait();
                    player?.Stop();
                    media?.Dispose();
                    player?.Dispose();
                    vlc?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing media: {ex.Message}");
                }
                finally
                {
                    _playLock.Release();
                    _playLock.Dispose();
                }
            });
        }
    }
}
