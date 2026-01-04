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

        public VLCVideoPlayerControl()
        {
            this.InitializeComponent();
            ViewModel = new VLCVideoPlayerViewModel(this.DispatcherQueue);
            this.Unloaded += VLCVideoPlayerControl_Unloaded;
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
    }
}
