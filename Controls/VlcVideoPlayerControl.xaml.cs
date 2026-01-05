using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Photo.ViewModels;
using LibVLCSharp.Platforms.Windows;

namespace Photo.Controls
{
    public sealed partial class VLCVideoPlayerControl : UserControl
    {
        public VLCVideoPlayerViewModel ViewModel { get; }
        private DispatcherTimer _volumeTimer;

        public VLCVideoPlayerControl()
        {
            this.InitializeComponent();
            ViewModel = new VLCVideoPlayerViewModel(this.DispatcherQueue);
            this.Unloaded += VLCVideoPlayerControl_Unloaded;

            _volumeTimer = new DispatcherTimer();
            _volumeTimer.Interval = TimeSpan.FromMilliseconds(500);
            _volumeTimer.Tick += (s, e) =>
            {
                VolumePanel.Visibility = Visibility.Collapsed;
                _volumeTimer.Stop();
            };
        }

        private void VideoView_Initialized(object sender, InitializedEventArgs e)
        {
            ViewModel.Initialize(e);
        }

        private void VLCVideoPlayerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.Dispose();
        }

        private void ContentArea_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ViewModel.ToggleControlsCommand.Execute(null);
        }

        private void VolumeButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _volumeTimer.Stop();
            VolumePanel.Visibility = Visibility.Visible;
            VolumePanel.UpdateLayout();

            var transform = VolumeButton.TransformToVisual(ContentArea);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            var buttonCenter = point.X + VolumeButton.ActualWidth / 2;
            var panelWidth = VolumePanel.ActualWidth;

            var left = buttonCenter - (panelWidth / 2);
            if (left < 0) left = 0;
            if (left + panelWidth > ContentArea.ActualWidth) left = ContentArea.ActualWidth - panelWidth;

            VolumePanel.Margin = new Thickness(left, 0, 0, 64);
        }

        private void VolumeButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _volumeTimer.Start();
        }

        private void VolumeControl_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _volumeTimer.Stop();
            VolumePanel.Visibility = Visibility.Visible;
        }

        private void VolumeControl_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _volumeTimer.Start();
        }
    }
}
