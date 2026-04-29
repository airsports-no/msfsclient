using Microsoft.UI.Xaml.Data;
using System;

namespace msfsclient.Helpers.Converters
{
    public class ConnectionStatusToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isConnected && isConnected)
            {
                return "SimConnect Connected";
            }
            return "SimConnect Disconnected";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
