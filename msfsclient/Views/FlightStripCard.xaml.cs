using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using msfsclient.Core.Api;
using msfsclient.Helpers;

namespace msfsclient.Views
{
    /// <summary>One flight as a strip, with its links (live map or my track, share, flight orders, directions, cancel).</summary>
    public sealed partial class FlightStripCard : UserControl
    {
        public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
            nameof(Item), typeof(StripItem), typeof(FlightStripCard), new PropertyMetadata(null, (d, _) => ((FlightStripCard)d).Bindings?.Update()));

        public FlightStripCard()
        {
            this.InitializeComponent();
        }

        public StripItem? Item
        {
            get => (StripItem?)GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        private static msfsclient.ViewModels.ShellViewModel Vm => App.Shell;

        private static StripItem? ItemOf(object sender) => (sender as FrameworkElement)?.Tag as StripItem;

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender)?.Target.MapLink is not { } link) return;
            var text = $"Follow my flight live: {Urls.SiteUrl(link)}";
            if (!Vm.Platform.ShareText("Follow my flight live", text))
            {
                _ = Dialogs.InfoAsync(XamlRoot, "Link copied", "The live link is on the clipboard. Paste it where you want to share it.");
            }
        }

        private void LiveMap_Click(object sender, RoutedEventArgs e) => Vm.Controller.OpenMap(ItemOf(sender)?.Target.MapLink);

        private void AllMap_Click(object sender, RoutedEventArgs e) => Vm.Controller.OpenMap(ItemOf(sender)?.Target.AllMapLink);

        /// <summary>Flight orders are a PDF download: the browser handles that better than the embedded page.</summary>
        private void FlightOrders_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender)?.Target.FlightOrderPath is { } path) Vm.Platform.OpenUri(Urls.SiteUrl(path));
        }

        /// <summary>The contest's page on the website opens in the browser (the live map stays in the client).</summary>
        private void ContestPage_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender)?.Target.ContestPagePath is { } path) Vm.Platform.OpenUri(Urls.SiteUrl(path));
        }

        private void Directions_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender)?.Target.DirectionsUrl is { } url) Vm.Platform.OpenUri(url);
        }

        private void Website_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender)?.Target.WebsiteUrl is { } url) Vm.Platform.OpenUri(url);
        }

        private async void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender)?.Cancellable is not { } flight) return;
            if (await FlightDialogs.ConfirmCancelAsync(XamlRoot, flight, Vm.NowMillis)) _ = Vm.Controller.CancelFlightAsync(flight);
        }
    }
}
