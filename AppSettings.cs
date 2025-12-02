using Windows.Storage;

namespace Photo
{
    public static class AppSettings
    {
        private static readonly ApplicationDataContainer LocalSettings = ApplicationData.Current.LocalSettings;

        private const string ConfirmBeforeDeleteKey = "ConfirmBeforeDelete";

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
    }
}
