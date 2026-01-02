using Microsoft.UI.Xaml.Controls;

namespace Photo
{
    public sealed partial class SettingsDialog : ContentDialog
    {
        public bool ConfirmBeforeDelete
        {
            get => DeleteConfirmToggle.IsOn;
            set => DeleteConfirmToggle.IsOn = value;
        }

        public bool ShowFaces
        {
            get => ShowFacesToggle.IsOn;
            set => ShowFacesToggle.IsOn = value;
        }

        public bool UseHardwareAcceleration
        {
            get => HardwareAccelerationToggle.IsOn;
            set => HardwareAccelerationToggle.IsOn = value;
        }

        public SettingsDialog()
        {
            this.InitializeComponent();
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // 点击确定时不需要额外处理，调用方会读取属性值
        }
    }
}
