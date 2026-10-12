using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace msfsclient.Helpers
{
    /// <summary>Small functions for x:Bind (visibility from text, negation, images from URLs, theme brushes).</summary>
    public static class Ui
    {
        public static bool Not(bool value) => !value;

        public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

        public static Visibility ShowText(string? text) => string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;

        public static Visibility ShowAny<T>(IReadOnlyCollection<T>? items) => items is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

        public static string OrEmpty(string? text) => text ?? "";

        private static readonly Dictionary<string, BitmapImage> Images = [];

        /// <summary>An image for a URL; the same object for the same URL so a refresh does not reload it.</summary>
        public static ImageSource? Image(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (!Images.TryGetValue(url, out var image))
            {
                image = new BitmapImage(uri);
                if (Images.Count > 200) Images.Clear();
                Images[url] = image;
            }
            return image;
        }

        public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    }
}
