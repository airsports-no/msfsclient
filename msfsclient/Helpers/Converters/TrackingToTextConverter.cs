using Microsoft.UI.Xaml.Data;
using System;

namespace msfsclient.Helpers.Converters
{
    public class TrackingToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isTracking && isTracking)
            {
                return "Stop Tracking";
            }
            return "Start Tracking";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
