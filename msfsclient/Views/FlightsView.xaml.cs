using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using msfsclient.Core.Api;
using msfsclient.Core.Tracking;
using msfsclient.Core.Ui;

namespace msfsclient.Views
{
    public sealed partial class FlightsView : ShellPart
    {
        private IReadOnlyList<MyFlight>? _flightsFor;
        private int? _cancellingFor;
        private bool _pastFor;
        private bool _syncingTabs;

        public FlightsView()
        {
            this.InitializeComponent();
        }

        public IReadOnlyList<DayGroup> Days { get; private set; } = [];

        public bool ShowLoading => Vm.State.FlightsLoading;

        public bool ShowList => !Vm.State.FlightsLoading && Vm.State.Flights.Count > 0;

        public bool ShowEmpty => !Vm.State.FlightsLoading && Vm.State.Flights.Count == 0;

        public bool CanRefresh => !Vm.State.FlightsLoading;

        public bool ShowFindEvent => !Vm.State.ShowPastFlights;

        public string EmptyText => Vm.State.ShowPastFlights ? "No past flights yet." : "You have no upcoming flights.";

        /// <summary>The list is rebuilt only when what it shows changed, so the 1 s refresh does not redraw it.</summary>
        protected override void Refresh()
        {
            var s = Vm.State;
            _syncingTabs = true;
            Tabs.SelectedItem = s.ShowPastFlights ? PastItem : UpcomingItem;
            _syncingTabs = false;
            if (!ReferenceEquals(s.Flights, _flightsFor) || s.ShowPastFlights != _pastFor || s.CancellingFlightId != _cancellingFor)
            {
                _flightsFor = s.Flights;
                _pastFor = s.ShowPastFlights;
                _cancellingFor = s.CancellingFlightId;
                var now = Vm.NowMillis;
                var nowUtc = System.DateTimeOffset.FromUnixTimeMilliseconds(now);
                Days = s.Flights
                    .GroupBy(f => TimeFormat.DayHeading(f.TakeoffTime, now))
                    .Select(day => new DayGroup(day.Key, day.Select(f => new StripItem(
                        TrackingTargets.FlightTarget(f, t => TimeFormat.FormatTimeLocal(t), t => TimeFormat.FormatLocal(t), nowUtc),
                        Live: false,
                        // Only for tasks that allow self-registration; also while the flight is underway.
                        Cancellable: !s.ShowPastFlights && f.CanCancel() ? f : null,
                        Cancelling: s.CancellingFlightId == f.Id,
                        Past: s.ShowPastFlights)).ToList()))
                    .ToList();
            }
        }

        private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            if (_syncingTabs) return;
            var past = ReferenceEquals(sender.SelectedItem, PastItem);
            if (past != Vm.State.ShowPastFlights) _ = Vm.Controller.ShowPastFlightsAsync(past);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.ReloadFlightsAsync();

        private void FindEvent_Click(object sender, RoutedEventArgs e) => Vm.Controller.OpenToday();
    }
}
