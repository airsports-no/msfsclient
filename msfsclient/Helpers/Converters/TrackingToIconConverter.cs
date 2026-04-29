using Microsoft.UI.Xaml.Data;
using System;

namespace msfsclient.Helpers.Converters
{
    public class TrackingToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isTracking && isTracking)
            {
                return "\uE71A"; // Stop icon
            }
            return "\uE768"; // Play icon
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
