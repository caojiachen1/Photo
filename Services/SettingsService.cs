using Windows.Storage;

namespace Photo.Services
{
    /// <summary>
    /// 设置服务接口
    /// </summary>
    public interface ISettingsService
    {
        bool ConfirmBeforeDelete { get; set; }
    }

    /// <summary>
    /// 设置服务实现
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly ApplicationDataContainer _localSettings;
        private const string ConfirmBeforeDeleteKey = "ConfirmBeforeDelete";

        public SettingsService()
        {
            _localSettings = ApplicationData.Current.LocalSettings;
        }

        public bool ConfirmBeforeDelete
        {
            get
            {
                var value = _localSettings.Values[ConfirmBeforeDeleteKey];
                return value is bool b ? b : false;
            }
            set
            {
                _localSettings.Values[ConfirmBeforeDeleteKey] = value;
            }
        }
    }
}
