using Windows.Storage;

namespace Photo
{
    public static class AppSettings
    {
        private static readonly ApplicationDataContainer LocalSettings = ApplicationData.Current.LocalSettings;

        private const string ConfirmBeforeDeleteKey = "ConfirmBeforeDelete";
        private const string ShowFacesKey = "ShowFaces";
        private const string UseHardwareAccelerationKey = "UseHardwareAcceleration";

        /// <summary>
        /// 删除文件前是否显示确认对话框
        /// </summary>
        public static bool ConfirmBeforeDelete
        {
            get
            {
                var value = LocalSettings.Values[ConfirmBeforeDeleteKey];
                return value is bool b ? b : false; // 默认关闭
            }
            set
            {
                LocalSettings.Values[ConfirmBeforeDeleteKey] = value;
            }
        }

        /// <summary>
        /// 是否显示人脸框
        /// </summary>
        public static bool ShowFaces
        {
            get
            {
                var value = LocalSettings.Values[ShowFacesKey];
                return value is bool b ? b : true; // 默认开启
            }
            set
            {
                LocalSettings.Values[ShowFacesKey] = value;
            }
        }

        /// <summary>
        /// 是否使用硬件加速
        /// </summary>
        public static bool UseHardwareAcceleration
        {
            get
            {
                var value = LocalSettings.Values[UseHardwareAccelerationKey];
                return value is bool b ? b : true; // 默认开启
            }
            set
            {
                LocalSettings.Values[UseHardwareAccelerationKey] = value;
            }
        }
    }
}
