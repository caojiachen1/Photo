using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Photo.Services;
using Windows.Storage;

namespace Photo.Controls
{
    public sealed partial class FFmpegVideoPlayerControl : UserControl
    {
        private FFmpegVideoPlayer? _player;
        private bool _isSeeking;
        private double _seekTargetPosition = -1; // seek 目标位置，避免进度条闪回
        private DispatcherTimer? _hideControlsTimer;
        private DispatcherTimer? _positionUpdateTimer;
        private bool _isLooping = false;
        private bool _isMuted = false;
        private bool _isShortVideo = false; // 短视频标记，用于精确拖拽

        public FFmpegVideoPlayerControl()
        {
            InitializeComponent();
            
            Loaded += FFmpegVideoPlayerControl_Loaded;
            Unloaded += FFmpegVideoPlayerControl_Unloaded;
            
            // 点击视频区域切换控件显示
            VideoImage.Tapped += VideoImage_Tapped;
            
            // 设置可获取焦点
            this.IsTabStop = true;
            this.AllowFocusOnInteraction = true;
            
            // 获取焦点以接收键盘事件
            this.GotFocus += OnControlGotFocus;
            this.PointerPressed += OnControlPointerPressed;
        }
        
        private void OnControlGotFocus(object sender, RoutedEventArgs e)
        {
            // 确保控件能接收键盘事件
        }
        
        private void OnControlPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 点击时获取焦点以接收键盘事件
            Focus(FocusState.Pointer);
        }

        private void UserControl_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Space)
            {
                e.Handled = true;
                TogglePlayPause();
                ShowControls();
                RestartHideTimer();
            }
            else if (e.Key == Windows.System.VirtualKey.Left)
            {
                e.Handled = true;
                if (_player != null)
                {
                    // 短视频使用更小的步进
                    var step = _isShortVideo ? 1.0 : 5.0;
                    var newPosition = Math.Max(0, _player.Position - step);
                    _player.Seek(newPosition);
                    ShowControls();
                    RestartHideTimer();
                }
            }
            else if (e.Key == Windows.System.VirtualKey.Right)
            {
                e.Handled = true;
                if (_player != null)
                {
                    // 短视频使用更小的步进
                    var step = _isShortVideo ? 1.0 : 5.0;
                    var newPosition = Math.Min(_player.Duration, _player.Position + step);
                    _player.Seek(newPosition);
                    ShowControls();
                    RestartHideTimer();
                }
            }
            else if (e.Key == Windows.System.VirtualKey.M)
            {
                // M 键静音切换
                e.Handled = true;
                if (_player != null)
                {
                    _isMuted = !_isMuted;
                    _player.IsMuted = _isMuted;
                    UpdateVolumeIcon();
                    ShowControls();
                    RestartHideTimer();
                }
            }
            else if (e.Key == Windows.System.VirtualKey.L)
            {
                // L 键循环切换
                e.Handled = true;
                _isLooping = !_isLooping;
                RepeatIcon.Foreground = _isLooping 
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(128, 255, 255, 255));
                ShowControls();
                RestartHideTimer();
            }
        }

        private void TogglePlayPause()
        {
            if (_player == null)
                return;

            if (_player.IsPlaying)
            {
                _player.Pause();
            }
            else
            {
                _player.Play();
            }
        }

        private void FFmpegVideoPlayerControl_Loaded(object sender, RoutedEventArgs e)
        {
            // 自动隐藏控件定时器
            _hideControlsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _hideControlsTimer.Tick += HideControlsTimer_Tick;

            // 位置更新定时器
            _positionUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionUpdateTimer.Tick += PositionUpdateTimer_Tick;

            ProgressSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ProgressSlider_PointerPressed), true);
            ProgressSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ProgressSlider_PointerReleased), true);
            ProgressSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(ProgressSlider_PointerCaptureLost), true);
            
            // 暂停位置更新定时器当拖动时
            ProgressSlider.GotFocus += (s, e) => _isSeeking = true;
            ProgressSlider.LostFocus += (s, e) => { _isSeeking = false; _player?.Seek(ProgressSlider.Value); };
        }

        private void FFmpegVideoPlayerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _hideControlsTimer?.Stop();
            _positionUpdateTimer?.Stop();
            
            if (_player != null)
            {
                _player.FrameReady -= Player_FrameReady;
                _player.PlaybackStarted -= Player_PlaybackStarted;
                _player.PlaybackPaused -= Player_PlaybackPaused;
                _player.PlaybackEnded -= Player_PlaybackEnded;
                _player.Dispose();
                _player = null;
            }
        }

        public void LoadVideo(StorageFile file)
        {
            try
            {
                LoadingRing.IsActive = true;

                _player?.Dispose();
                _player = new FFmpegVideoPlayer(DispatcherQueue);
                
                _player.FrameReady += Player_FrameReady;
                _player.PlaybackStarted += Player_PlaybackStarted;
                _player.PlaybackPaused += Player_PlaybackPaused;
                _player.PlaybackEnded += Player_PlaybackEnded;

                if (_player.Open(file.Path))
                {
                    ProgressSlider.Maximum = _player.Duration;
                    
                    // 根据视频时长判断是否为短视频，并设置更精确的步进
                    _isShortVideo = _player.Duration < 60;
                    double stepFrequency;
                    if (_player.Duration < 10)
                    {
                        // 非常短的视频：帧级精度
                        stepFrequency = _player.FrameRate > 0 ? 1.0 / _player.FrameRate : 0.033;
                    }
                    else if (_player.Duration < 60)
                    {
                        // 短视频：0.1秒精度
                        stepFrequency = 0.1;
                    }
                    else if (_player.Duration < 600)
                    {
                        // 中等视频：0.5秒精度
                        stepFrequency = 0.5;
                    }
                    else
                    {
                        // 长视频：1秒精度
                        stepFrequency = 1.0;
                    }
                    ProgressSlider.StepFrequency = stepFrequency;
                    ProgressSlider.SmallChange = stepFrequency;
                    ProgressSlider.LargeChange = stepFrequency * 10;
                    
                    TotalTimeText.Text = FormatTime(_player.Duration);
                    _player.Play();
                    _positionUpdateTimer?.Start();
                    ShowControls();
                    
                    // 获取焦点以接收键盘事件
                    Focus(FocusState.Programmatic);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Failed to open video file");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading video: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        public void Stop()
        {
            _player?.Stop();
            _positionUpdateTimer?.Stop();
        }

        private void Player_FrameReady(object? sender, WriteableBitmap bitmap)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                VideoImage.Source = bitmap;
            });
        }

        private void Player_PlaybackStarted(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdatePlayPauseIcon(true);
                _positionUpdateTimer?.Start();
            });
        }

        private void Player_PlaybackPaused(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdatePlayPauseIcon(false);
            });
        }

        private void Player_PlaybackEnded(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isLooping && _player != null)
                {
                    _player.Seek(0);
                    _player.Play();
                }
                else
                {
                    UpdatePlayPauseIcon(false);
                    _positionUpdateTimer?.Stop();
                }
            });
        }

        private void PositionUpdateTimer_Tick(object? sender, object e)
        {
            if (_player == null || _isSeeking)
                return;

            // 如果播放器正在 seek，保持显示目标位置
            if (_player.IsSeeking && _seekTargetPosition >= 0)
            {
                ProgressSlider.Value = _seekTargetPosition;
                CurrentTimeText.Text = FormatTime(_seekTargetPosition);
                return;
            }

            // 如果有 seek 目标位置，检查是否已接近目标
            if (_seekTargetPosition >= 0)
            {
                var currentPos = _player.Position;
                // 当播放器位置接近 seek 目标时，清除目标位置
                if (Math.Abs(currentPos - _seekTargetPosition) < 0.5)
                {
                    _seekTargetPosition = -1;
                }
                else
                {
                    // 还未到达目标，显示目标位置而非当前位置
                    ProgressSlider.Value = _seekTargetPosition;
                    CurrentTimeText.Text = FormatTime(_seekTargetPosition);
                    TotalTimeText.Text = FormatTime(_player.Duration);
                    return;
                }
            }

            ProgressSlider.Value = _player.Position;
            CurrentTimeText.Text = FormatTime(_player.Position);
            TotalTimeText.Text = FormatTime(_player.Duration);
        }

        private string FormatTime(double seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            if (time.TotalHours >= 1)
                return time.ToString(@"h\:mm\:ss");
            // 短视频显示毫秒
            if (_isShortVideo && seconds < 60)
                return time.ToString(@"mm\:ss\.f");
            return time.ToString(@"mm\:ss");
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null)
                return;

            if (_player.IsPlaying)
            {
                _player.Pause();
            }
            else
            {
                _player.Play();
            }

            RestartHideTimer();
        }

        private void UpdatePlayPauseIcon(bool isPlaying)
        {
            PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768"; // Pause : Play
        }

        private void ProgressSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isSeeking = true;
            _positionUpdateTimer?.Stop();
        }

        private void ProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isSeeking && _player != null)
            {
                var targetPosition = ProgressSlider.Value;
                _seekTargetPosition = targetPosition;
                _player.Seek(targetPosition);
            }
            _isSeeking = false;
            // 延迟启动定时器，给 seek 一些时间完成
            DispatcherQueue.TryEnqueue(() => _positionUpdateTimer?.Start());
        }

        private void ProgressSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_isSeeking && _player != null)
            {
                var targetPosition = ProgressSlider.Value;
                _seekTargetPosition = targetPosition;
                _player.Seek(targetPosition);
            }
            _isSeeking = false;
            // 延迟启动定时器，给 seek 一些时间完成
            DispatcherQueue.TryEnqueue(() => _positionUpdateTimer?.Start());
        }

        private void ProgressSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isSeeking)
            {
                CurrentTimeText.Text = FormatTime(e.NewValue);
            }
        }

        private void RewindButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null)
                return;

            // 短视频使用更小的步进
            var step = _isShortVideo ? 3.0 : 10.0;
            var newPosition = Math.Max(0, _player.Position - step);
            _player.Seek(newPosition);
            RestartHideTimer();
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null)
                return;

            // 短视频使用更小的步进
            var step = _isShortVideo ? 3.0 : 10.0;
            var newPosition = Math.Min(_player.Duration, _player.Position + step);
            _player.Seek(newPosition);
            RestartHideTimer();
        }

        private void VolumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null)
                return;

            _isMuted = !_isMuted;
            _player.IsMuted = _isMuted;
            UpdateVolumeIcon();
            RestartHideTimer();
        }

        private void UpdateVolumeIcon()
        {
            VolumeIcon.Glyph = _isMuted ? "\uE74F" : "\uE767"; // Mute : Volume
        }

        private void VideoImage_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (TransportControlsPanel.Visibility == Visibility.Visible)
            {
                HideControls();
            }
            else
            {
                ShowControls();
            }
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            _isLooping = !_isLooping;
            RepeatIcon.Foreground = _isLooping 
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(128, 255, 255, 255));
        }

        private void ShowControls()
        {
            TransportControlsPanel.Visibility = Visibility.Visible;
            FadeInStoryboard.Begin();
        }

        private void HideControls()
        {
            FadeOutStoryboard.Completed -= FadeOutStoryboard_Completed;
            FadeOutStoryboard.Completed += FadeOutStoryboard_Completed;
            FadeOutStoryboard.Begin();
            _hideControlsTimer?.Stop();
        }

        private void FadeOutStoryboard_Completed(object? sender, object e)
        {
            TransportControlsPanel.Visibility = Visibility.Collapsed;
        }

        private void RestartHideTimer()
        {
            _hideControlsTimer?.Stop();
            _hideControlsTimer?.Start();
        }

        private void HideControlsTimer_Tick(object? sender, object e)
        {
            HideControls();
        }
    }
}
