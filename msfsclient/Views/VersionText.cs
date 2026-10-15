using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace msfsclient.Views
{
    /// <summary>
    /// "Airsports 2.0.0 (30)": quiet and easy to find (login, home, profile, update required), what to quote when reporting
    /// a problem. Clicking it seven times in a row opens the hidden debug view (only once signed in).
    /// </summary>
    public sealed partial class VersionText : UserControl
    {
        private int _taps;
        private long _lastTap;

        public VersionText()
        {
            Content = new TextBlock { Text = App.Shell.VersionLine, Style = (Style)Application.Current.Resources["SmallText"] };
            Margin = new Thickness(0, 8, 0, 0);
            Tapped += OnTapped;
        }

        private void OnTapped(object sender, TappedRoutedEventArgs e)
        {
            var now = Environment.TickCount64;
            _taps = now - _lastTap > 2_000 ? 1 : _taps + 1; // a pause starts the count again
            _lastTap = now;
            if (_taps < 7) return;
            _taps = 0;
            var shell = App.Shell;
            if (!shell.State.IsOutside) shell.Controller.OpenDebug();
        }
    }
}
