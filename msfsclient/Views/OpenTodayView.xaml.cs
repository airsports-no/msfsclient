using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using msfsclient.Core.App;

namespace msfsclient.Views
{
    public sealed partial class OpenTodayView : ShellPart
    {
        private OpenTodayUi? _for;

        public OpenTodayView()
        {
            this.InitializeComponent();
        }

        public IReadOnlyList<VenueItem> Venues { get; private set; } = [];

        private OpenTodayUi Ui => Vm.State.OpenToday;

        public bool ShowLoading => Ui.Loading && Ui.Tasks.Count == 0;

        public bool ShowError => Ui.Error != null && Ui.Tasks.Count == 0;

        public string ErrorText => Ui.Error ?? "";

        public bool ShowList => Venues.Count > 0;

        public bool ShowEmpty => !Ui.Loading && Ui.Error == null && Venues.Count == 0;

        public bool CanRefresh => !Ui.Loading;

        /// <summary>The list is rebuilt only when what it shows changed (new tasks, or the aircraft's position for the sort).</summary>
        protected override void Refresh()
        {
            if (ReferenceEquals(Ui, _for)) return;
            _for = Ui;
            Venues = OpenRegistration.Group(Ui.Tasks, Ui.Here).Select(e => new VenueItem(e, Vm.NowMillis)).ToList();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.LoadOpenTodayAsync();

        private void Web_Click(object sender, RoutedEventArgs e) => Vm.Controller.OpenTodayOnTheWeb();

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is OpenTaskItem item) Vm.Controller.OpenPath(OpenRegistration.RegisterPath(item.Task));
        }

        private void Directions_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is VenueItem item && item.Venue.DirectionsUrl is { } url) Vm.Platform.OpenUri(url);
        }
    }
}
