using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace msfsclient.Helpers
{
    /// <summary>Confirmations and short explanations, worded as in the mobile app.</summary>
    public static class Dialogs
    {
        private static bool _open;

        /// <summary>A dialog is showing (WinUI shows one at a time).</summary>
        public static bool IsOpen => _open;

        /// <summary>
        /// Shows a dialog and returns whether the primary button was chosen. Only one dialog at a time (WinUI allows no more);
        /// a second request while one is open is answered "no".
        /// </summary>
        public static async Task<bool> ConfirmAsync(XamlRoot root, string title, object content, string primary, string? close, bool danger = false)
        {
            if (_open) return false;
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = title,
                Content = content is string text ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["BodyText"] } : content,
                PrimaryButtonText = primary,
                DefaultButton = danger ? ContentDialogButton.Close : ContentDialogButton.Primary,
            };
            if (close != null) dialog.CloseButtonText = close;
            if (danger)
            {
                var style = new Style(typeof(Button));
                style.Setters.Add(new Setter(Control.ForegroundProperty, Application.Current.Resources["ErrorBrush"]));
                dialog.PrimaryButtonStyle = style;
            }
            _open = true;
            try
            {
                return await dialog.ShowAsync() == ContentDialogResult.Primary;
            }
            finally
            {
                _open = false;
            }
        }

        public static Task InfoAsync(XamlRoot root, string title, string text) => ConfirmAsync(root, title, text, "OK", null);
    }
}
