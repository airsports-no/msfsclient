using Microsoft.UI.Xaml.Data;
using System;

namespace msfsclient.Helpers.Converters
{
    public class DoubleFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double d)
            {
                string format = parameter as string ?? "F2";
                return d.ToString(format);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
