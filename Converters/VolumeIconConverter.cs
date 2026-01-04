using Microsoft.UI.Xaml.Data;
using System;

namespace Photo.Converters
{
    public class VolumeIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isMuted)
            {
                // E74F = 静音图标 (Volume0)
                // E767 = 音量图标 (Volume)
                return isMuted ? "\uE74F" : "\uE767";
            }
            return "\uE767"; // 默认显示音量图标
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
