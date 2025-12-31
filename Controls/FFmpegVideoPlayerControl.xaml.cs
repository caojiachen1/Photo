using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Photo.Services;
using Windows.Storage;

namespace Photo.Controls
{
    public sealed partial class FFmpegVideoPlayerControl : UserControl
    {
        private FFmpegVideoPlayer? _player;
        private bool _isSeeking;
        private double _seekPosition;
        private DispatcherTimer? _hideControlsTimer;
        private DispatcherTimer? _positionUpdateTimer;
        private bool _isLooping = false;
        private bool _isMuted = false;

        public FFmpegVideoPlayerControl()
        {
            InitializeComponent();
            
            Loaded += FFmpegVideoPlayerControl_Loaded;
            Unloaded += FFmpegVideoPlayerControl_Unloaded;
            
            // 点击视频区域切换控件显示
            VideoImage.Tapped += VideoImage_Tapped;
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
                    TotalTimeText.Text = FormatTime(_player.Duration);
                    _player.Play();
                    _positionUpdateTimer?.Start();
                    ShowControls();
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

            ProgressSlider.Value = _player.Position;
            CurrentTimeText.Text = FormatTime(_player.Position);
            TotalTimeText.Text = FormatTime(_player.Duration);
        }

        private string FormatTime(double seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            if (time.TotalHours >= 1)
                return time.ToString(@"h\:mm\:ss");
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
            _seekPosition = ProgressSlider.Value;
            RestartHideTimer();
        }

        private void ProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isSeeking)
            {
                _isSeeking = false;
                _player?.Seek(_seekPosition);
            }
            RestartHideTimer();
        }

        private void ProgressSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_isSeeking)
            {
                _isSeeking = false;
                _player?.Seek(_seekPosition);
            }
        }

        private void ProgressSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isSeeking && _player != null)
            {
                _seekPosition = e.NewValue;
                CurrentTimeText.Text = FormatTime(e.NewValue);
            }
        }

        private void RewindButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null)
                return;

            var newPosition = Math.Max(0, _player.Position - 10);
            _player.Seek(newPosition);
            RestartHideTimer();
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null)
                return;

            var newPosition = Math.Min(_player.Duration, _player.Position + 10);
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
        }

        private void HideControls()
        {
            TransportControlsPanel.Visibility = Visibility.Collapsed;
            _hideControlsTimer?.Stop();
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
