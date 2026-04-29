using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace msfsclient.Helpers.Converters
{
    public class UrlToImageSourceConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string url && !string.IsNullOrWhiteSpace(url))
            {
                System.Diagnostics.Debug.WriteLine($"Converting URL to ImageSource: {url}");
                try
                {
                    return new BitmapImage(new Uri(url));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Image Conversion Error: {ex.Message}");
                    return null;
                }
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
