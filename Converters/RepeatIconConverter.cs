using Microsoft.UI.Xaml.Data;
using System;

namespace Photo.Converters
{
    public class RepeatIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isRepeat)
            {
                // E8ED = 循环图标 (RepeatOne/Repeat)
                // E8EE = 不循环图标 (RepeatOff)
                return isRepeat ? "\uE8EE" : "\uF5E7";
            }
            return "\uF5E7"; // 默认显示不循环图标
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
