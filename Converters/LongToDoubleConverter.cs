using Microsoft.UI.Xaml.Data;
using System;

namespace Photo.Converters
{
    public class LongToDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is long longValue)
            {
                return (double)longValue;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is double doubleValue)
            {
                return (long)doubleValue;
            }
            return 0L;
        }
    }
}
