using Microsoft.UI.Xaml.Data;
using System;

namespace Photo
{
    /// <summary>
    /// 将播放状态转换为播放/暂停图标字形
    /// </summary>
    public class PlayPauseIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isPlaying)
            {
                // E769 = Pause, E768 = Play
                return isPlaying ? "\uE769" : "\uE768";
            }
            return "\uE768"; // Default to Play icon
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
