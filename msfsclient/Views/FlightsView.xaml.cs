using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using msfsclient.Core.Api;

namespace msfsclient.Views
{
    public sealed partial class FlightsView : ShellPart
    {
        private IReadOnlyList<MyFlight>? _flightsFor;
        private int? _cancellingFor;

        public FlightsView()
        {
            this.InitializeComponent();
        }

        public IReadOnlyList<FlightItem> Flights { get; private set; } = [];

        public string EmptyText => Vm.State.FlightsLoading ? "Loading…"
            : Vm.State.Flights.Count == 0 ? (Vm.State.ShowPastFlights ? "No past flights." : "You have no upcoming flights.") : "";

        protected override void Refresh()
        {
            var s = Vm.State;
            UpcomingTab.IsChecked = !s.ShowPastFlights;
            PastTab.IsChecked = s.ShowPastFlights;
            if (!ReferenceEquals(s.Flights, _flightsFor) || s.CancellingFlightId != _cancellingFor)
            {
                _flightsFor = s.Flights;
                _cancellingFor = s.CancellingFlightId;
                Flights = s.Flights.Select(f => new FlightItem(f, s.ShowPastFlights, s.CancellingFlightId)).ToList();
            }
        }

        private void Upcoming_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.State.ShowPastFlights) _ = Vm.Controller.ShowPastFlightsAsync(false);
            Refresh();
        }

        private void Past_Click(object sender, RoutedEventArgs e)
        {
            if (!Vm.State.ShowPastFlights) _ = Vm.Controller.ShowPastFlightsAsync(true);
            Refresh();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is FlightItem item) Vm.Controller.OpenMap(item.Flight.MapLink);
        }

        private async void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not FlightItem item) return;
            if (await FlightDialogs.ConfirmCancelAsync(XamlRoot, item.Flight, Vm.NowMillis)) _ = Vm.Controller.CancelFlightAsync(item.Flight);
        }
    }
}
