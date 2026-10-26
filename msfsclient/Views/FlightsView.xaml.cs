using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using msfsclient.Core.Api;
using msfsclient.Core.App;
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
        private int _visit = -1;
        private string _query = "";
        private string? _queryFor;
        private int _shownCount;
        private bool _syncingSearch;
        // The scroll position of each tab (upcoming, past). This page is kept while a map is open over it, but the position is
        // also restored explicitly when it is shown again or its list is rebuilt, which would otherwise start at the top.
        private readonly double[] _offsets = new double[2];
        private bool _restoring;

        public FlightsView()
        {
            this.InitializeComponent();
            Loaded += (_, _) => RestoreScrollSoon();
        }

        public IReadOnlyList<DayGroup> Days { get; private set; } = [];

        public bool ShowLoading => Vm.State.FlightsLoading;

        public bool ShowList => !Vm.State.FlightsLoading && _shownCount > 0;

        public bool ShowSearch => Vm.State.ShowPastFlights && Vm.State.Flights.Count > 0;

        public bool ShowNoMatch => !Vm.State.FlightsLoading && Vm.State.Flights.Count > 0 && _shownCount == 0;

        public string NoMatchText => $"Nothing matches “{_query.Trim()}”.";

        public bool ShowEmpty => !Vm.State.FlightsLoading && Vm.State.Flights.Count == 0;

        public bool CanRefresh => !Vm.State.FlightsLoading;

        public bool ShowFindEvent => !Vm.State.ShowPastFlights;

        public string EmptyText => Vm.State.ShowPastFlights ? "No past flights yet." : "You have no upcoming flights.";

        /// <summary>The list is rebuilt only when what it shows changed, so the 1 s refresh does not redraw it.</summary>
        protected override void Refresh()
        {
            var s = Vm.State;
            if (s.FlightsVisit != _visit)
            {
                // A new visit to My flights starts at the top with no search; coming back from a map does not.
                _visit = s.FlightsVisit;
                _query = "";
                _syncingSearch = true;
                SearchBox.Text = "";
                _syncingSearch = false;
                _offsets[0] = _offsets[1] = 0;
                _flightsFor = null;
            }
            _syncingTabs = true;
            Tabs.SelectedItem = s.ShowPastFlights ? PastItem : UpcomingItem;
            _syncingTabs = false;
            if (!ReferenceEquals(s.Flights, _flightsFor) || s.ShowPastFlights != _pastFor || s.CancellingFlightId != _cancellingFor || _query != _queryFor)
            {
                _flightsFor = s.Flights;
                _pastFor = s.ShowPastFlights;
                _cancellingFor = s.CancellingFlightId;
                _queryFor = _query;
                var now = Vm.NowMillis;
                var nowUtc = System.DateTimeOffset.FromUnixTimeMilliseconds(now);
                var shown = s.ShowPastFlights ? FlightFilter.Filter(s.Flights, _query) : s.Flights;
                _shownCount = shown.Count;
                Days = shown
                    .GroupBy(f => TimeFormat.DayHeading(f.TakeoffTime, now))
                    .Select(day => new DayGroup(day.Key, day.Select(f => new StripItem(
                        TrackingTargets.FlightTarget(f, t => TimeFormat.FormatTimeLocal(t), t => TimeFormat.FormatLocal(t), nowUtc),
                        Live: false,
                        // Only for tasks that allow self-registration; also while the flight is underway.
                        Cancellable: !s.ShowPastFlights && f.CanCancel() ? f : null,
                        Cancelling: s.CancellingFlightId == f.Id,
                        Past: s.ShowPastFlights)).ToList()))
                    .ToList();
                RestoreScrollSoon();
            }
        }

        /// <summary>Puts the list back where the pilot left it, once the rebuilt list has been laid out.</summary>
        private void RestoreScrollSoon()
        {
            _restoring = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                ListScroll.UpdateLayout();
                ListScroll.ChangeView(null, _offsets[_pastFor ? 1 : 0], null, disableAnimation: true);
                _restoring = false;
            });
        }

        private void ListScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!_restoring) _offsets[_pastFor ? 1 : 0] = ListScroll.VerticalOffset;
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingSearch) return;
            _query = SearchBox.Text;
            _offsets[1] = 0;
            Refresh();
            Notify();
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
